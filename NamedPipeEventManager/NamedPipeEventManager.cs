using log4net;
using log4net.Config;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;


namespace VoiceCmdManager
{

    public class NamedPipeEventManager
    {
        private readonly ILog log = LogManager.GetLogger(typeof(NamedPipeEventManager));
        private const int BUFFER_SIZE = 4096;

        private string  _channelName = "Default";
        private string  _inPipeName = "VoiceCmdManager.Default.In";
        private string  _outPipeName = "VoiceCmdManager.Default.Out";
        private int     _maxServerInstances = 10;
        private string  _pipeStartMsg = "StartListening";
        private string  _pipeStopMsg = "StopListening";
        
        private PipeSecurity _pipeSecurity;
        private Dictionary<string, EventPipe> _pipes = new Dictionary<string, EventPipe>();

        public delegate void PipeConnHandler(string pipeId);
        public delegate void PipeMessageHandler(PipeMessage msg, string channelName, string pipeId);

        public PipeConnHandler OnConnected;
        public PipeConnHandler OnDisconnected;
        public PipeMessageHandler OnMessage;


        private void Initialize(string channelName, string inPipeName, string outPipeName, int maxServerInstances, string pipeStartMsg, string pipeStopMsg)
        {
            XmlConfigurator.Configure(new FileInfo("logging.config"));

            _channelName = channelName;
            _inPipeName = inPipeName;
            _outPipeName = outPipeName;
            _maxServerInstances = maxServerInstances;
            _pipeStartMsg = pipeStartMsg;
            _pipeStopMsg = pipeStopMsg;

            _pipeSecurity = new PipeSecurity();
            var usersSid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
            var usersRule = new PipeAccessRule(usersSid, PipeAccessRights.ReadWrite, AccessControlType.Allow);
            _pipeSecurity.AddAccessRule(usersRule);
            using (WindowsIdentity self = WindowsIdentity.GetCurrent())
            {
                var selfRule = new PipeAccessRule(self.Name, PipeAccessRights.FullControl, AccessControlType.Allow);
                _pipeSecurity.AddAccessRule(selfRule);
            }
        }

        // inPipe is mainly used to receive control message to STOP the connection
        // outPipe is used to send out the message to external components such as download status or purchase status
        public NamedPipeEventManager(string name, string inPipeName, string outPipeName, int maxServerInstances, string pipeStartMsg, string pipeStopMsg)
        {
            Initialize(name, inPipeName, outPipeName, maxServerInstances, pipeStartMsg, pipeStopMsg);
        }

        // use default settings to initialize the manager 
        public NamedPipeEventManager()
        {
           Initialize(_channelName, _inPipeName, _outPipeName, _maxServerInstances, _pipeStartMsg, _pipeStopMsg);
        }

        public void Start()
        {
            try
            {
                // Init named pipe
                for (int i = 0; i < _maxServerInstances; i++)
                {
                    EventPipe pipe = new EventPipe(_inPipeName, _outPipeName, _maxServerInstances, _pipeSecurity);
                    pipe.Start(InConnectionCallBack, OutConnectionCallBack);
                    _pipes.Add(pipe.Id, pipe);
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
            foreach (var pipeKV in _pipes)
            {
                EventPipe pipe = pipeKV.Value;
                pipe.Stop();
            }
        }

        public void SendMsg(PipeMessage msg)
        {
            foreach (var pipeKV in _pipes)
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
                EventPipe pipe = _pipes[pipeId];
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
            foreach (var pipeKV in _pipes)
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
            EventPipe pipe = _pipes[pipeId];

            try
            {
                if (pipe.OutPipe != null && pipe.OutPipe.CanWrite)
                {
                    pipe.OutPipe.EndWaitForConnection(result);

                    if (_pipeStartMsg != null)
                    {
                        int readBytes = pipe.OutPipe.Read(pipe.OutMsgBuf, 0, BUFFER_SIZE);
                        string msg = ToString(pipe.OutMsgBuf, readBytes);

                        // Check start message
                        if (readBytes <= 0 || msg != _pipeStartMsg)
                        {
                            log.Error($"HandleMessage: empty content or invalid start message: " + msg);
                            RestartConnectionWaiting(pipe.Id);
                            return;
                        }
                    }

                    // Send ack message
                    log.Debug($"Pipe {pipe.Id} Connected.");
                    PipeMessage ackMsg = new PipeMessage("Ack", $"Command pipe Connected! Id: {pipe.Id}, PipeName: {_outPipeName}");
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
            EventPipe pipe = _pipes[pipeId];

            try
            {
                if (pipe.InPipe != null && pipe.InPipe.CanRead)
                {
                    pipe.InPipe.EndWaitForConnection(result);

                    // Start async read for connection status checking
                    pipe.InPipe.BeginRead(pipe.InMsgBuf, 0, BUFFER_SIZE, BeginReadCallback, pipe.Id);
                }
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
            EventPipe pipe = _pipes[pipeId];

            int readBytes = pipe.InPipe.EndRead(result);
            string msg = ToString(pipe.InMsgBuf, readBytes);
            if (readBytes > 0 && 
                (_pipeStopMsg == null || (_pipeStopMsg != null && msg != _pipeStopMsg)))
            {
                try {
                    PipeMessage pmsg = JsonConvert.DeserializeObject<PipeMessage>(msg);
                    log.Debug($"Client message received, type: {pmsg.type}, msg: {pmsg.message} ");

                    if (OnMessage != null)
                    {
                        OnMessage(pmsg, _channelName, pipeId);
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

        private void RestartConnectionWaiting(string pipeId)
        {
            log.Info("RestartWaiting!");
            EventPipe pipe = null;
      
            try
            {
                pipe = _pipes[pipeId];
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
