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
using System.Threading;
//using VoiceCmdManager;


namespace VoiceCmdManager
{
    public class Program
    {
        static readonly ILog log = LogManager.GetLogger(typeof(Program));

        const int MAX_CONNECTIONS_A_PIPE = 1;

        // Control message's event type
        const string EVENT_TYPE_ECHO = "Echo";
        const string EVENT_TYPE_UPDATE_GRAMMAR = "UpdateGrammar";

        // Error code
        const string LOCAL_MUTEX_NAME = @"Local\VoiceCmdManager.main.exe";
        const int DEFAULT_EXIT_CODE = 1000;
        const int ALREADY_RUNNING_EXIT_CODE = 1001;
        const int STOP_EXIT_CODE = 1002;
        const int ABNORMAL_EXIT_CODE = 1003;

        // named pipe collection for different clients
        static Dictionary<string, NamedPipeEventManager> pipeMap = new Dictionary<string, NamedPipeEventManager>();
        static VoiceCmdRecognizer voiceCmdRecognizer;


        static void Main(string[] args)
        {
            XmlConfigurator.Configure(new FileInfo("logging.config"));

            bool mutexCreated = false;
            Mutex mutex = new Mutex(true, LOCAL_MUTEX_NAME, out mutexCreated);
            Environment.ExitCode = DEFAULT_EXIT_CODE;

            try
            {
                if (!mutexCreated)
                {
                    log.Debug("VoiceCmdManager already running");

                    Environment.ExitCode = ALREADY_RUNNING_EXIT_CODE;
                }
                else
                {
                    Environment.ExitCode = ABNORMAL_EXIT_CODE;
                    Start();

                    Console.WriteLine("Press enter to exit.");
                    Console.ReadLine();

                    Stop();
                    Console.ReadLine();
                    Environment.ExitCode = STOP_EXIT_CODE;
                }
            }
            finally
            {
                mutex.Dispose();
            }
        }

        static void Start()
        {
            try
            {
                // read settings
                VoiceCmdSetting[] vsArray = JsonConvert.DeserializeObject<VoiceCmdSetting[]>(File.ReadAllText("VoiceCmdSetting.json"));

                // Init voice command recognizer
                if (voiceCmdRecognizer == null)
                {
                    voiceCmdRecognizer = new VoiceCmdRecognizer(VoiceCmdHandler);
                }

                foreach (VoiceCmdSetting vs in vsArray)
                {
                    if (!pipeMap.ContainsKey(vs.channelName)) {
                        NamedPipeEventManager nmgr = new NamedPipeEventManager(vs.channelName, vs.ctrlPipeName, vs.cmdPipeName, MAX_CONNECTIONS_A_PIPE, null, null);

                        // load grammar
                        voiceCmdRecognizer.loadGrammer(vs);

                        // Attach call back function
                        nmgr.OnConnected += ConnectedHandler;
                        nmgr.OnDisconnected += DisconnectedHandler;
                        nmgr.OnMessage += CtrlMessageHandler;

                        // Add pipe to dictionary
                        pipeMap[vs.channelName] = nmgr;
                    } else
                    {
                        log.Warn("Duplicate voice pipe for " + vs.channelName + ", ignore this setting record.");
                    }
                }
                
                // Start app pipe server
                foreach (var pipe in pipeMap)
                {
                    pipe.Value.Start();
                }

            }
            catch (Exception ex)
            {
                log.Error(ex);
                throw ex;
            }
        }

        static void ConnectedHandler(string pipeId)
        {
            voiceCmdRecognizer.StartListening();
        }

        static void DisconnectedHandler(string pipeId)
        {
            bool doNotStop = false;

            foreach (var pipe in pipeMap)
            {
                if (pipe.Value.GetConnectedPipeCount() > 0)
                {
                    doNotStop = true;
                    break;
                }
            }

            if (!doNotStop)
            {
                voiceCmdRecognizer.StopListening();
            }
        }

        static void CtrlMessageHandler(PipeMessage pmsg, string channelName, string pipeId)
        {
            VoiceCmdSetting.VoiceGrammar grammarToUpdate;
            try
            {
                switch (pmsg.type)
                {
                    case EVENT_TYPE_ECHO:
                        if (pipeMap.ContainsKey(channelName))
                        {
                            pipeMap[channelName].SendMsg(pmsg);
                        }
                        break;
                    case EVENT_TYPE_UPDATE_GRAMMAR:
                        grammarToUpdate = JsonConvert.DeserializeObject<VoiceCmdSetting.VoiceGrammar>(pmsg.message);
                        voiceCmdRecognizer.UpdateGrammar(channelName, grammarToUpdate);
                        break;
                    default:
                        log.Debug($"no matched event type {pmsg.type}, msg: {pmsg.message}");
                        break;
                }

            }
            catch (Exception ex)
            {
                log.Error($"Failed in processing control message, msg: {pmsg.message}, ex: {ex}");
            }
        }

        static void Stop()
        {
            foreach (var pipe in pipeMap)
            {
                pipe.Value.Stop();
            }
        }

        static void VoiceCmdHandler(string msg, string grammarName, float confidence)
        {
            log.Debug($"Voice message received: {grammarName}, {msg}, {confidence}");

            // dispatch by channel
            string channel = grammarName.Split('.')[0];
            string grammarType = grammarName.Split('.')[1];

            PipeMessage pmsg = new PipeMessage(grammarType, msg);

            if (pipeMap.ContainsKey(channel))
            {
                pipeMap[channel].SendMsg(pmsg);
            } else
            {
                log.Debug($"Channel [{channel}] of recognised message doesn't exist anymore, ignore the message [{msg}]");
            }
        }
    }
}

