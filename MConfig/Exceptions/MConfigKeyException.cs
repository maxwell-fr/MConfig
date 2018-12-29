using System;
using System.Collections.Generic;
using System.Text;

namespace MConfig
{

    [Serializable]
    public class MConfigKeyException : Exception
    {
        public MConfigKeyException() { }
        public MConfigKeyException(string message) : base(message) { }
        public MConfigKeyException(string message, Exception inner) : base(message, inner) { }
        protected MConfigKeyException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }
}
