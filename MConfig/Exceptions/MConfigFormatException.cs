using System;
using System.Collections.Generic;
using System.Text;

namespace MConfig
{

    [Serializable]
    public class MConfigFormatException : Exception
    {
        public MConfigFormatException() { }
        public MConfigFormatException(string message) : base(message) { }
        public MConfigFormatException(string message, Exception inner) : base(message, inner) { }
        protected MConfigFormatException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }
}
