using System;
using System.Collections.Generic;
using System.IO;


namespace MConfig
{
    public class MConfigurator : IMConfigurator
    {
        private const int CONFIG_SIZE = 8_192;
        private const int CURRENT_VERSION = 0;
        private const int HEADER_LENGTH = 5;

        private byte[] Buffer;
        private string Secret;
        private Stream Stream;
        private bool StreamIsMine;
        private bool IsDirty;

        
        private IDictionary<string, string> ConfigData;

        /// <summary>
        /// Instantiate a new configurator using a provided stream.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="secret"></param>
        public MConfigurator(Stream stream, string secret = null)
        {
            StreamIsMine = false;
            Setup(stream, secret);
        }

        /// <summary>
        /// Instantiate a new configurator using a filename.
        /// The file will be created if it does not exist.
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="secret"></param>
        public MConfigurator(string filename, string secret = null)
        {
            StreamIsMine = true;
            Setup(new FileStream(filename, FileMode.OpenOrCreate, FileAccess.ReadWrite), secret);
        }

        /// <summary>
        /// Do common setup work.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="secret"></param>
        void Setup(Stream stream, string secret)
        {
            ConfigData = new Dictionary<string, string>();

            IsDirty = false;
            
            Buffer = new byte[CONFIG_SIZE];
            SetSecret(secret);
            Stream = stream;

            ReadAndDecode();
        }

        /// <summary>
        /// Retrieve the value associated with a given key.
        /// </summary>
        /// <param name="key">The key to find.</param>
        /// <returns>The value. Empty (but present) keys return null.</returns>
        public string Get(string key)
        {
            if(!ContainsKey(key))
            {
                throw new MConfigKeyException($"Key {key} not present.");
            }
            else
            {
                return ConfigData[key];
            }
        }

        /// <summary>
        /// Add or update a value associated with the given key.
        /// </summary>
        /// <param name="key">The key to use. If the key exists it will be updated.</param>
        /// <param name="value">The value to associate with the key.</param>
        public void Add(string key, string value)
        {
            if(key.Length > byte.MaxValue)
            {
                throw new MConfigFormatException($"Key too long: {key}.");
            }
            if(value != null && value.Length > byte.MaxValue)
            {
                throw new MConfigFormatException($"Value too long: {value}.");
            }

            ConfigData[key] = value;
            IsDirty = true;
        }


        /// <summary>
        /// Check if a key exists.
        /// </summary>
        /// <param name="key">The key to check.</param>
        /// <returns>True if present, false otherwise.</returns>
        public bool ContainsKey(string key)
        {
            return ConfigData.ContainsKey(key);
        }

        /// <summary>
        /// Removes a key and any associated value.
        /// </summary>
        /// <param name="key">The key to remove.</param>
        public void Remove(string key)
        {
            if (!ContainsKey(key))
            {
                throw new MConfigKeyException($"Key {key} not present.");
            }
            else
            {
                ConfigData.Remove(key);
            }
        }

        /// <summary>
        /// Number of items present.
        /// </summary>
        public int Count
        {
            get
            {
                return ConfigData.Count;
            }
        }


        public void SetSecret(string secret)
        {
            Secret = secret;
            IsDirty = true;
        }

        /// <summary>
        /// Convenience indexer to allow square-bracket access.
        /// </summary>
        /// <param name="key">The key to use.</param>
        /// <returns>The value.</returns>
        public string this[string key]
        {
            get
            {
                return Get(key);
            }

            set
            {
                Add(key, value);
            }
        }




        void ReadAndDecode()
        {
            Stream.Seek(0, SeekOrigin.Begin);
            int actualLength = Stream.Read(Buffer, 0, CONFIG_SIZE);

            // at least this many bytes in the header (magic + version), or it's a new file
            if (actualLength < HEADER_LENGTH)
            {
                IsDirty = true;
            }
            else
            {
                DeobfuscateBuffer();

                if (!(Buffer[0] == 0x4d && Buffer[1] == 0x43 && Buffer[2] == 0x4f &&
                      Buffer[3] == 0x4e && Buffer[4] == 0x46))
                {
                    throw new MConfigFormatException("Invalid file format.");
                }
                //FUTURE: Buffer[5] holds version

                for (int i = HEADER_LENGTH + 1; i < actualLength; ++i)
                {
                    //get and check key length byte
                    //this being zero indicates end of file (i.e., the zero padding)
                    byte keyLength = Buffer[i];
                    if (keyLength == 0)
                    {
                        break;
                    }

                    //check that we have that many bytes left
                    if (i + keyLength >= actualLength)
                    {
                        throw new MConfigFormatException($"Key length ${keyLength} truncated at {i}.");
                    }
                    //advance to start of key and decode
                    ++i;
                    string key = System.Text.Encoding.UTF8.GetString(Buffer, i, keyLength);

                    ConfigData[key] = null;

                    //advance to value length
                    i += keyLength;
                    if (i >= actualLength) //end of container
                    {
                        break;
                    }

                    //get and check value length byte
                    //zero means null value; key present but unset
                    byte valLength = Buffer[i];
                    if (valLength != 0)
                    {
                        //check that we have that many bytes left
                        if (i + valLength >= actualLength)
                        {
                            throw new MConfigFormatException($"Value length ${valLength} truncated at {i}.");
                        }
                        //advance to start of value and copy into value buffer
                        ++i;
                        string value = System.Text.Encoding.UTF8.GetString(Buffer, i, valLength);

                        ConfigData[key] = value;

                        i += valLength - 1; // -1 to account for next-iteration goodness
                    }
                }
            }
        }

        /// <summary>
        /// Encode the internal dictionary to the buffer.
        /// </summary>
        void EncodeToBuffer()
        {
            Array.Clear(Buffer, 0, CONFIG_SIZE);

            Buffer[0] = 0x4d;
            Buffer[1] = 0x43;
            Buffer[2] = 0x4f;
            Buffer[3] = 0x4e;
            Buffer[4] = 0x46;
            Buffer[5] = CURRENT_VERSION;

            int bufIndex = HEADER_LENGTH + 1;

            foreach (string key in ConfigData.Keys)
            {
                string value = ConfigData[key];

                byte[] keyBytes = System.Text.Encoding.UTF8.GetBytes(key);
                if (keyBytes.Length > byte.MaxValue)
                {
                    throw new MConfigFormatException($"Key too long: [{key}].");
                }
                byte keyLength = (byte)keyBytes.Length;

                byte valLength = 0;
                byte[] valBytes = { };
                if (value != null)
                {
                    valBytes = System.Text.Encoding.UTF8.GetBytes(value);
                    if (valBytes.Length > byte.MaxValue)
                    {
                        throw new MConfigFormatException($"Value too long: [{value}].");
                    }
                    valLength = (byte)valBytes.Length;
                }


                //check to make sure we won't run over
                if (bufIndex + 1 + keyLength + 1 + valLength + 1> CONFIG_SIZE)
                {
                    throw new MConfigFormatException($"Maximum size will be exceeded at key [{key}].");
                }

                Buffer[bufIndex] = keyLength;
                ++bufIndex;
                for (int i = 0; i < keyLength; ++i)
                {
                    Buffer[bufIndex] = keyBytes[i];
                    ++bufIndex;
                }

                Buffer[bufIndex] = valLength;
                ++bufIndex;
                for (int i = 0; i < valLength; ++i)
                {
                    Buffer[bufIndex] = valBytes[i];
                    ++bufIndex;
                }
            }
            //ending byte
            Buffer[bufIndex] = 0;

            //fill rest with random
            Random rand = new Random();
            for(bufIndex += 1; bufIndex < CONFIG_SIZE; ++bufIndex)
            {
                Buffer[bufIndex] = (byte)rand.Next(0, 255);
            }

            ObfuscateBuffer();
        }

        /// <summary>
        /// Save to the backing stream.
        /// </summary>
        public void Save()
        {
            EncodeToBuffer();

            Stream.Seek(0, SeekOrigin.Begin);
            Stream.SetLength(CONFIG_SIZE);

            Stream.Write(Buffer, 0, CONFIG_SIZE);

            IsDirty = false;
        }

        /*
         *  File Format Detail:
         *  
         *  4d 43 4f 4e 46 vv 
         *  ll xx xx xx xx xx mm yy yy yy yy yy
         *  
         *  first five are magic bytes
         *  v = version byte
         *  x = key, y = value
         *  l = length of key in bytes, m = length of value in bytes 
         *  pattern repeats
         *  
         */

        /// <summary>
        /// Apply obfuscation to the buffer contents (excluding the header).
        /// Does nothing if secret is null.
        /// </summary>
        private void ObfuscateBuffer()
        {
            XorBuffer();
        }

        /// <summary>
        /// Remove the obfuscation from the buffer. Does nothing if secret is null.
        /// </summary>
        private void DeobfuscateBuffer()
        {
            XorBuffer();
        }

        /// <summary>
        /// Simple xor algorithm for obfuscation
        /// </summary>
        private void XorBuffer()
        {
            if (Secret == null || Secret.Length == 0)
            {
                return;
            }
            byte[] secret = System.Text.Encoding.UTF8.GetBytes(Secret);
            int s = 0;

            for (int i = HEADER_LENGTH + 1; i < CONFIG_SIZE; ++i)
            {
                Buffer[i] = (byte)(Buffer[i] ^ secret[s]);
                ++s;
                if (s >= secret.Length)
                {
                    s = 0;
                }
            }
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    if (IsDirty)
                    {
                        Save();
                    }
                    if (StreamIsMine)
                    {
                        Stream.Close();
                    }
                }

                disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion

    }
}
