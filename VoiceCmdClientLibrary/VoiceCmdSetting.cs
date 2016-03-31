using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace VoiceCmdManager
{
    public class VoiceCmdSetting
    {
        public class VoiceGrammar
        {
            public string type;
            public string startWord;
            public float confidence;
            public string[] patterns;
        }

        public string channelName;
        public string cmdPipeName;
        public string ctrlPipeName;
        public VoiceGrammar[] grammars;
    }
}


