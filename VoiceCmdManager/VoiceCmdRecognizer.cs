using log4net;
using log4net.Config;
using System;
using System.Collections.Generic;
using System.IO;
using System.Speech.Recognition;
using System.Linq;


namespace VoiceCmdManager
{
    public class VoiceCmdRecognizer
    {
        public delegate void VoiceCmdHandler(string msg, string grammarName, float confidence);

        private readonly ILog log = LogManager.GetLogger(typeof(VoiceCmdRecognizer));
        private VoiceCmdHandler s_cmdHandler;
        private SpeechRecognitionEngine s_recognizer;
        private Dictionary<string, Grammar> grammarMap = new Dictionary<string, Grammar>();
        private Dictionary<string, float> confidenceMap = new Dictionary<string, float>();
        private bool s_isListening = false;
        private bool s_isLoaded = false;
        private object initLock = new Object();

        private class GrammarUpdateObject
        {
            public Grammar grammarToUpdate;
            public float confidence;

            public GrammarUpdateObject(Grammar g, float c)
            {
                grammarToUpdate = g;
                confidence = c;
            }
        }

        public VoiceCmdRecognizer(VoiceCmdHandler cmdHandler)
        {
            XmlConfigurator.Configure(new FileInfo("logging.config"));
            s_cmdHandler = cmdHandler;
            s_recognizer = new SpeechRecognitionEngine();
        }

        public void StartListening()
        {
            lock(initLock)
            {
                if (!s_isLoaded)
                {
                    prepareGrammar();

                    // Attach event handlers to the recognizer.
                    s_recognizer.SpeechRecognized +=
                      new EventHandler<SpeechRecognizedEventArgs>(
                        SpeechRecognizedHandler);
                    s_recognizer.RecognizeCompleted +=
                      new EventHandler<RecognizeCompletedEventArgs>(
                        RecognizeCompletedHandler);
                    s_recognizer.RecognizerUpdateReached +=
                      new EventHandler<RecognizerUpdateReachedEventArgs>(
                        RecognizerUpdateReached);

                    s_recognizer.BabbleTimeout = new TimeSpan(0);
                    s_recognizer.InitialSilenceTimeout = new TimeSpan(0);
                    log.Debug("BabbleTimeout:" + s_recognizer.BabbleTimeout + ",InitialSilenceTimeout:" + s_recognizer.InitialSilenceTimeout);

                    // Assign input to the recognizer.
                    s_recognizer.SetInputToDefaultAudioDevice();

                    s_isLoaded = true;
                }

                if (!s_isListening)
                {

                    // Start to listen.
                    s_recognizer.RecognizeAsync(RecognizeMode.Multiple);

                    // Keep state
                    s_isListening = true;
                }
            }
        }

        // prepare default grammars
        private void prepareGrammar()
        {
            // Grammer for garbage collecting
            DictationGrammar dg = new DictationGrammar("grammar:dictation#pronunciation");
            dg.Name = "Random";

            // Create the question dictation grammar.
            //DictationGrammar msgDictationGrammar =
            //  new DictationGrammar("grammar:dictation");
            //msgDictationGrammar.Name = "dictation";
            //msgDictationGrammar.Enabled = true;
           
            s_recognizer.LoadGrammar(dg);
            //s_recognizer.LoadGrammar(msgDictationGrammar);
            //msgDictationGrammar.SetDictationContext("speech start", null);

        }

        public void StopListening()
        {
            if (s_isListening)
            {
                log.Debug("stop listening");
                s_recognizer.RecognizeAsyncCancel();
                s_isListening = false;
            }
        }

        public Grammar ToGrammar(string channelName, VoiceCmdSetting.VoiceGrammar voiceGrammar)
        {
            GrammarBuilder gb = new GrammarBuilder();

            // prepare grammar
            if (voiceGrammar.startWord != null)
            {
                gb.Append(voiceGrammar.startWord);
            }
            gb.Append(new Choices(voiceGrammar.patterns));

            // grammar name is channelName.grammar_type
            Grammar g = new Grammar(gb);
            g.Name = channelName + "." + voiceGrammar.type;

            gb = null;

            return g;
        }

        public void loadGrammer(VoiceCmdSetting vs)
        {
            foreach (VoiceCmdSetting.VoiceGrammar voiceGrammar in vs.grammars)
            {
                Grammar g = ToGrammar(vs.channelName, voiceGrammar);

                if (!grammarMap.ContainsKey(g.Name))
                {
                    // load grammar
                    s_recognizer.LoadGrammar(g);
                    // add grammar to map
                    grammarMap[g.Name] = g;
                    confidenceMap[g.Name] = voiceGrammar.confidence;
                } else
                {
                    log.Warn("Ignore duplicate grammar: " + g.Name);
                }               
            }
        }

        public void UpdateGrammar(string channelName, VoiceCmdSetting.VoiceGrammar voiceGrammar)
        {
            Grammar grammarToUpdate = ToGrammar(channelName, voiceGrammar);
            s_recognizer.RequestRecognizerUpdate(new GrammarUpdateObject(grammarToUpdate, voiceGrammar.confidence));
        }

        private void RecognizerUpdateReached(object sender, RecognizerUpdateReachedEventArgs e)
        {
            log.Debug("RecognizerUpdateReached!");
            GrammarUpdateObject guo = (GrammarUpdateObject)e.UserToken;
            Grammar grammar = guo.grammarToUpdate;
            float confidence = guo.confidence;

            // update grammar
            if (grammarMap.ContainsKey(grammar.Name))
            {
                s_recognizer.UnloadGrammar(grammarMap[grammar.Name]);
            }

            s_recognizer.LoadGrammar(grammar);
            grammarMap[grammar.Name] = grammar;

            // update confidence if it has value
            if (confidence > 0)
            {
                confidenceMap[grammar.Name] = confidence;
            }
        }

        private void SpeechRecognizedHandler(object sender, SpeechRecognizedEventArgs e)
        {
           // log.Debug("speech recognized");

            if (e.Result != null)
            {
                // return all possible result
                if (e.Result.Grammar.Name != "Random")
                {
                    foreach (RecognizedPhrase phrase in e.Result.Alternates)
                    {
                        float confidence = 0;

                        if (confidenceMap.ContainsKey(phrase.Grammar.Name))
                        {
                            confidence = confidenceMap[phrase.Grammar.Name];
                        }

                        // filter response by confidence threshold
                        if (phrase.Confidence >= confidence)
                        {
                            s_cmdHandler(phrase.Text, phrase.Grammar.Name, phrase.Confidence);
                        }
                        else
                        {
                            log.Debug($"Result is filtered confidence too low; {phrase.Grammar.Name}, {phrase.Text}, {phrase.Confidence}");
                        }
                    }
                }
            }
            else
            {
                log.Debug("VOICE not recognized");
            }
        }

        private void RecognizeCompletedHandler(object sender, RecognizeCompletedEventArgs e)
        {
            log.Debug("Voice recognizer completed. reason?" + (e.Error != null?e.Error.Message:"null") + "," + e.Cancelled + "," + e.InputStreamEnded);
            StopListening();
        }

        private string[] loadCommands(string filepath)
        {
            StreamReader file = new StreamReader(filepath);
            List<string> cmdList = new List<string>();
            string cmd;
            
            while ((cmd = file.ReadLine()) != null)
            {
                if (cmd.Trim() != String.Empty)
                {
                    log.Debug("Loaded command: " + cmd.Trim());
                    cmdList.Add(cmd.Trim());
                }
            }
            
            return cmdList.ToArray();
        }

    }
}
