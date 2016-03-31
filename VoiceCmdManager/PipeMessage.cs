using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HTC.VR.Marketplace.StoreServices.ServiceImplementations.NamedPipeEventManager
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
