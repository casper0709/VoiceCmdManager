using log4net;
using log4net.Config;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;


namespace HTC.VR.Marketplace.StoreServices.ServiceImplementations.NamedPipeEventManager
{

    public class NamedPipeEventManager
    {
        private readonly ILog log = LogManager.GetLogger(typeof(NamedPipeEventManager));
        private const int BUFFER_SIZE = 4096;

        private string s_InPipeName = "HTC.VR.Marketplace.StoreServices.In";
        private string s_OutPipeName = "HTC.VR.Marketplace.StoreServices.Out";
        private int s_maxServerInstances = 10;
        private string s_pipeStartMsg = "StartListening";
        private string s_pipeStopMsg = "StopListening";
        
        private PipeSecurity s_pipeSecurity;
        private Dictionary<string, EventPipe> s_pipes = new Dictionary<string, EventPipe>();

        public delegate void PipeConnHandler(string pipeId);
        public delegate void PipeMessageHandler(PipeMessage msg, string pipeId);

        public PipeConnHandler OnConnected;
        public PipeConnHandler OnDisconnected;
        public PipeMessageHandler OnMessage;


        private void Initialize(string inPipeName, string outPipeName, int maxServerInstances, string pipeStartMsg, string pipeStopMsg)
        {
            XmlConfigurator.Configure(new FileInfo("logging.config"));

            s_InPipeName = inPipeName;
            s_OutPipeName = outPipeName;
            s_maxServerInstances = maxServerInstances;
            s_pipeStartMsg = pipeStartMsg;
            s_pipeStopMsg = pipeStopMsg;

            s_pipeSecurity = new PipeSecurity();
            var usersSid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
            var usersRule = new PipeAccessRule(usersSid, PipeAccessRights.ReadWrite, AccessControlType.Allow);
            s_pipeSecurity.AddAccessRule(usersRule);
            using (WindowsIdentity self = WindowsIdentity.GetCurrent())
            {
                var selfRule = new PipeAccessRule(self.Name, PipeAccessRights.FullControl, AccessControlType.Allow);
                s_pipeSecurity.AddAccessRule(selfRule);
            }
        }

        // inPipe is mainly used to receive control message to STOP the connection
        // outPipe is used to send out the message to external components such as download status or purchase status
        public NamedPipeEventManager(string inPipeName, string outPipeName, int maxServerInstances, string pipeStartMsg, string pipeStopMsg)
        {
            Initialize(inPipeName, outPipeName, maxServerInstances, pipeStartMsg, pipeStopMsg);
        }

        // use default settings to initialize the manager 
        public NamedPipeEventManager()
        {
           Initialize(s_InPipeName, s_OutPipeName, s_maxServerInstances, s_pipeStartMsg, s_pipeStopMsg);
        }

        public void Start()
        {
            try
            {
                // Init named pipe
                for (int i = 0; i < s_maxServerInstances; i++)
                {
                    EventPipe pipe = new EventPipe(s_InPipeName, s_OutPipeName, s_maxServerInstances, s_pipeSecurity);
                    pipe.Start(InConnectionCallBack, OutConnectionCallBack);
                    s_pipes.Add(pipe.Id, pipe);
                }
            }
            catch (Exception ex)
            {
                log.Error(ex);
                throw ex;
            }
        }

        public void Stop()
        {
            foreach (var pipeKV in s_pipes)
            {
                EventPipe pipe = pipeKV.Value;
                pipe.Stop();
            }
        }

        public void SendMsg(PipeMessage msg)
        {
            foreach (var pipeKV in s_pipes)
            {
                EventPipe pipe = pipeKV.Value;
                try {                       
                    if (pipe.OutPipe.IsConnected && pipe.OutPipe.CanWrite)
                    {
                        log.Debug($"Sending to {pipe.Id}, message: {msg}");
                        byte[] msgBytes = Encoding.UTF8.GetBytes(msg.ToString());
                        pipe.OutPipe.Write(msgBytes, 0, msgBytes.Length);
                        pipe.OutPipe.Flush();
                    }
                }
                catch (Exception ex)
                {
                    log.Debug($"Write message failed. Pipe {pipe.Id} should have broken. ex: {ex}");
                    if (OnDisconnected != null)
                    {
                        OnDisconnected(pipe.Id);
                    }
                    RestartConnectionWaiting(pipe.Id);
                }
            }
        }

        public void SendMsg(PipeMessage msg, string pipeId)
        {
            try
            {
                EventPipe pipe = s_pipes[pipeId];
                if (pipe.OutPipe.IsConnected && pipe.OutPipe.CanWrite)
                {
                    log.Debug($"Sending to {pipe.Id}, message: {msg}");
                    byte[] msgBytes = Encoding.UTF8.GetBytes(msg.ToString());
                    pipe.OutPipe.Write(msgBytes, 0, msgBytes.Length);
                    pipe.OutPipe.Flush();
                }
            }
            catch (Exception ex)
            {
                log.Debug($"Write message failed. Pipe {pipeId} should have broken. ex: {ex}");
                if (OnDisconnected != null)
                {
                    OnDisconnected(pipeId);
                }
                RestartConnectionWaiting(pipeId);
            }
        }

        public int GetConnectedPipeCount()
        {
            int count = 0;
            foreach (var pipeKV in s_pipes)
            {
                if (pipeKV.Value.Status == ConnStatus.Connected)
                {
                    count++;
                }
            }
            return count;
        }

        private void OutConnectionCallBack(IAsyncResult result)
        {
            string pipeId = (string)result.AsyncState;
            EventPipe pipe = s_pipes[pipeId];

            try
            {
                pipe.OutPipe.EndWaitForConnection(result);

                if (s_pipeStartMsg != null)
                {
                    int readBytes = pipe.OutPipe.Read(pipe.OutMsgBuf, 0, BUFFER_SIZE);
                    string msg = ToString(pipe.OutMsgBuf, readBytes);

                    // Check start message
                    if (readBytes <= 0 || msg != s_pipeStartMsg)
                    {
                        log.Error($"HandleMessage: empty content or invalid start message: " + msg);
                        RestartConnectionWaiting(pipe.Id);
                        return;
                    }
                }

                // Send ack message
                log.Debug($"Pipe {pipe.Id} Connected.");
                PipeMessage ackMsg = new PipeMessage("Ack", $"Command pipe Connected! Id: {pipe.Id}, PipeName: {s_OutPipeName}");
                byte[] ackMsgBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(ackMsg));
                pipe.OutPipe.Write(ackMsgBytes, 0, ackMsgBytes.Length);

                // Update status
                pipe.Status = ConnStatus.Connected;

                // Run user action
                if (OnConnected != null)
                {
                    OnConnected(pipeId);
                }
            }
            catch (Exception ex)
            {
                log.Error("Exception while waiting for connection to OutPipe, restart waiting , ex: " + ex);
                RestartConnectionWaiting(pipe.Id);
            }
        }

        private void InConnectionCallBack(IAsyncResult result)
        {
            string pipeId = (string)result.AsyncState;
            EventPipe pipe = s_pipes[pipeId];

            try
            {
                pipe.InPipe.EndWaitForConnection(result);

                // Start async read for connection status checking
                pipe.InPipe.BeginRead(pipe.InMsgBuf, 0, BUFFER_SIZE, BeginReadCallback, pipe.Id);
            }
            catch (Exception ex)
            {
                log.Error("Exception in BeginRead for InPipe, restart waiting. ex: " + ex);
                RestartConnectionWaiting(pipe.Id);
            }
        }

        private void BeginReadCallback(IAsyncResult result)
        {
            string pipeId = (string)result.AsyncState;
            EventPipe pipe = s_pipes[pipeId];

            int readBytes = pipe.InPipe.EndRead(result);
            string msg = ToString(pipe.InMsgBuf, readBytes);
            if (readBytes > 0 && (s_pipeStopMsg != null && msg != s_pipeStopMsg))
            {
                try {
                    PipeMessage pmsg = JsonConvert.DeserializeObject<PipeMessage>(msg);
                    log.Debug($"Client message received, type: {pmsg.type}, msg: {pmsg.message} ");

                    // Embed echo function for client testing
                    Echo(pmsg, pipe.Id);

                    if (OnMessage != null)
                    {
                        OnMessage(pmsg, pipeId);
                    }
                } catch (Exception ex)
                {
                    log.Error($"Failed to process received message: {msg}, ex: {ex}");
                }

                pipe.InPipe.BeginRead(pipe.InMsgBuf, 0, BUFFER_SIZE, BeginReadCallback, pipe.Id);
            }
            else
            {
                log.Debug("Receive empty or stop message, client disconnected or broken.");
                RestartConnectionWaiting(pipe.Id);
            }
        }

        private void Echo(PipeMessage msg, string pipeId)
        {
            if (msg.type.ToLower() == "echo")
            {
                SendMsg(msg, pipeId);
            }
        }

        private void RestartConnectionWaiting(string pipeId)
        {
            log.Info("RestartWaiting!");
            EventPipe pipe = null;
      
            try
            {
                pipe = s_pipes[pipeId];
                pipe.Stop();
            }
            catch (Exception ex)
            {
                log.Warn("Clean up connection failed: " + ex);
            }
            finally
            {
                // Restart stream to wait for new connection
                if (pipe != null)
                {
                    pipe.Start(InConnectionCallBack, OutConnectionCallBack);
                }
            }
        }

        private string ToString(byte[] buf, int length)
        {
            return Encoding.UTF8.GetString(buf, 0, length);
        }
    }
}
