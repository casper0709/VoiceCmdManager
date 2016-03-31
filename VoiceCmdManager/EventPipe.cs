using log4net;
using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HTC.VR.Marketplace.StoreServices.ServiceImplementations.NamedPipeEventManager
{
    public enum ConnStatus
    {
        Disconnected,
        Waiting,
        Connected
    }

    public class EventPipe
    {
        private readonly ILog log = LogManager.GetLogger(typeof(EventPipe));
        private const int BUFFER_SIZE = 4096;

        private string s_InPipeName;
        private string s_OutPipeName;
        private int s_maxServerInstances;
        private PipeSecurity s_pipeSecurity;

        public string Id { get; private set; } = Guid.NewGuid().ToString();
        public NamedPipeServerStream InPipe { get; private set; }
        public NamedPipeServerStream OutPipe { get; private set; }
        public byte[] InMsgBuf { get; private set; } = new byte[BUFFER_SIZE];
        public byte[] OutMsgBuf { get; private set; } = new byte[BUFFER_SIZE];

        public ConnStatus Status = ConnStatus.Disconnected;

        public EventPipe(string inPipeName, string outPipeName, int maxServerInstances, PipeSecurity pipeSecurity)
        {
            s_InPipeName = inPipeName;
            s_OutPipeName = outPipeName;
            s_maxServerInstances = maxServerInstances;
            s_pipeSecurity = pipeSecurity;
        }

        public void Start(AsyncCallback inConnCB, AsyncCallback outConnCB)
        {
            try
            {
                if (InPipe == null && OutPipe == null)
                {
                    InPipe = new NamedPipeServerStream(s_InPipeName, PipeDirection.InOut, s_maxServerInstances, PipeTransmissionMode.Message, PipeOptions.Asynchronous | PipeOptions.WriteThrough, BUFFER_SIZE, BUFFER_SIZE, s_pipeSecurity);
                    OutPipe = new NamedPipeServerStream(s_OutPipeName, PipeDirection.InOut, s_maxServerInstances, PipeTransmissionMode.Message, PipeOptions.Asynchronous | PipeOptions.WriteThrough, BUFFER_SIZE, BUFFER_SIZE, s_pipeSecurity);
                    InPipe.BeginWaitForConnection(inConnCB, Id);
                    OutPipe.BeginWaitForConnection(outConnCB, Id);
                    Status = ConnStatus.Waiting;
                    log.Info($"Named pipe id: {Id}, Start waiting for client connection.");
                }
                else
                {
                    log.Warn("Try to Start pipe before stopping.");
                }
            }
            catch (Exception ex)
            {
                log.Error(ex);
                //throw ex;
            }
        }

        public void Stop()
        {
            if (InPipe != null)
            {
                if (InPipe.IsConnected) InPipe.Disconnect();
                InPipe.Close();
                InPipe.Dispose();
                InPipe = null;
            }

            if (OutPipe != null)
            {
                if (OutPipe.IsConnected) OutPipe.Disconnect();
                OutPipe.Close();
                OutPipe.Dispose();
                OutPipe = null;
            }

            Status = ConnStatus.Disconnected;
        }
    }
}
