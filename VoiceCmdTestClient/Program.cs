using log4net;
using log4net.Config;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;

namespace VoiceCmdManager
{
    class Program
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(Program));

        const string EVENT_TYPE_ECHO = "Echo";
        const int BufferSize = 4096;
        static byte[] MsgBuf = new byte[BufferSize];

        static NamedPipeClientStream SendStream;
        static NamedPipeClientStream RecvStream;
       
        static void StartListen(VoiceCmdSetting vs)
        {
            try
            {
                log.Debug($"try to connect to {vs.cmdPipeName}, {vs.ctrlPipeName}");
                // Connect to receive_pipe and send start message
                RecvStream = new NamedPipeClientStream(".", vs.cmdPipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.WriteThrough);
                RecvStream.Connect(500);
                RecvStream.ReadMode = PipeTransmissionMode.Message;

                // Connect to send_pipe
                SendStream = new NamedPipeClientStream(".", vs.ctrlPipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.WriteThrough);
                SendStream.Connect(500);
                SendStream.ReadMode = PipeTransmissionMode.Message;

                // Start listening to event
                RecvStream.BeginRead(MsgBuf, 0, BufferSize, EndReadCallBack, null);
            }
            catch (Exception ex)
            {
                // Swallow the exception
                log.Error("Namedpipe exception: " + ex.Message + "\n" + ex.StackTrace);
            }
        }

        static void StopListen()
        {
            try
            {
                log.Debug("StopListen.");
                SendStream.Close();
                SendStream.Dispose();
            }
            catch (Exception ex)
            {
                log.Error("Namedpipe exception while sending stop signal to server, ex: " + ex);
            }
        }

        static void EndReadCallBack(IAsyncResult result)
        {
            try
            {
                var readBytes = RecvStream.EndRead(result);
                if (readBytes > 0)
                {
                    string message = Encoding.UTF8.GetString(MsgBuf, 0, readBytes);
                    MsgHandler(message);
                    RecvStream.BeginRead(MsgBuf, 0, BufferSize, EndReadCallBack, null);
                }
                else // When no bytes were read, it can mean that the client have been disconnected
                {
                    log.Info("Named pipe received empty content, close pipe");
                    RecvStream.Close();
                    RecvStream.Dispose();
                }
            }
            catch (Exception ex)
            {
                log.Error("Named pipe error, close pipe. ex: " + ex);
                RecvStream.Close();
                RecvStream.Dispose();
            }
        }

        static void MsgHandler(string msg)
        {
            try
            {
                PipeMessage pmsg = JsonConvert.DeserializeObject<PipeMessage>(msg);
                //log.Debug($"Message received, type: {pmsg.type}, msg: {pmsg.message}");
                Console.WriteLine($"Message received, type: {pmsg.type}, msg: {pmsg.message}");
            }
            catch (Exception ex)
            {
                log.Error("Failed to deserialize received message, ex: " + ex);
            }
        }

        static void Main(string[] args)
        {
            string input = "";
            byte[] bytes2send;
            PipeMessage pmsg;

            XmlConfigurator.Configure(new FileInfo("logging.config"));

            // default config
            string confFile = "VoiceCmdClientSetting.json";

            try {
                if (args.Length >= 2)
                {
                    confFile = args[1];
                }

                // decide which configuration to use
                VoiceCmdSetting vs = JsonConvert.DeserializeObject<VoiceCmdSetting>(File.ReadAllText(confFile));

                StartListen(vs);

                Console.WriteLine("- Recognised voice messages will be listed below.");
                Console.WriteLine("- Enter anything to receive echo message.");
                Console.WriteLine("- Enter 'stop' to close program.\n\n");

                while (true)
                {
                    try
                    {
                        input = Console.ReadLine();
                        if (input == "stop")
                        {
                            Console.WriteLine("Stopping..");
                            StopListen();
                            break;
                        }
                        pmsg = new PipeMessage(EVENT_TYPE_ECHO, input);
                        bytes2send = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(pmsg));
                        SendStream.Write(bytes2send, 0, bytes2send.Length);
                        pmsg = null;
                        bytes2send = null;
                    }
                    catch (Exception ex)
                    {
                        log.Error("Namedpipe exception: ex: " + ex);
                    }
                }

                return;
            } catch (Exception ex)
            {
                log.Error("Failed to start test client, ex: " + ex);
                Console.WriteLine("enter to close program.");
                Console.ReadLine();
                return;
            }
        }
    }
}
