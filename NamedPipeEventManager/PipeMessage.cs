using Newtonsoft.Json;


namespace VoiceCmdManager
{
    public class PipeMessage
    {
        public string type;
        public string message;

        public PipeMessage(string type, string message)
        {
            this.type = type;
            this.message = message;
        }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}
