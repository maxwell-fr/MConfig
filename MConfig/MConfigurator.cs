using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.XPath;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;


namespace MConfig
{
    public sealed class MConfigurator : IMConfigurator, IConfigurationProvider
    {
        private const int ConfigSize = 8_192;
        private const int CurrentVersion = 0;
        private const int HeaderLength = 5;

        private byte[] _buffer;
        private string? _secret;
        private Stream _stream;
        private bool _streamIsMine;
        private bool _isDirty;

        
        private IDictionary<string, string?> _configData;

        /// <summary>
        /// Instantiate a new configurator using a provided stream.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="secret"></param>
        public MConfigurator(Stream stream, string? secret = null)
        {
            _streamIsMine = false;
            _configData = new Dictionary<string, string?>();

            _isDirty = false;
            
            _buffer = new byte[ConfigSize];
            SetSecret(secret);
            _stream = stream;

            ReadAndDecode();
        }

        /// <summary>
        /// Instantiate a new configurator using a filename.
        /// The file will be created if it does not exist.
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="secret"></param>
        public MConfigurator(string filename, string? secret = null)
            : this(new FileStream(filename, FileMode.OpenOrCreate, FileAccess.ReadWrite), secret)
        {
            _streamIsMine = true;
        }

        /// <summary>
        /// Retrieve the value associated with a given key.
        /// </summary>
        /// <param name="key">The key to find.</param>
        /// <returns>The value. Empty (but present) keys return null.</returns>
        public string? Get(string key)
        {
            if(!ContainsKey(key))
            {
                throw new MConfigKeyException($"Key {key} not present.");
            }
            else
            {
                return _configData[key];
            }
        }

        /// <summary>
        /// Add or update a value associated with the given key.
        /// </summary>
        /// <param name="key">The key to use. If the key exists it will be updated.</param>
        /// <param name="value">The value to associate with the key.</param>
        public void Add(string key, string? value)
        {
            if(key.Length > byte.MaxValue)
            {
                throw new MConfigFormatException($"Key too long: {key}.");
            }
            if(value is { Length: > byte.MaxValue })
            {
                throw new MConfigFormatException($"Value too long: {value}.");
            }

            _configData[key] = value;
            _isDirty = true;
        }


        /// <summary>
        /// Check if a key exists.
        /// </summary>
        /// <param name="key">The key to check.</param>
        /// <returns>True if present, false otherwise.</returns>
        public bool ContainsKey(string key)
        {
            return _configData.ContainsKey(key);
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
                _configData.Remove(key);
            }
        }

        /// <summary>
        /// Number of items present.
        /// </summary>
        public int Count => _configData.Count;


        public void SetSecret(string? secret)
        {
            _secret = secret;
            _isDirty = true;
        }

        /// <summary>
        /// Convenience indexer to allow square-bracket access.
        /// </summary>
        /// <param name="key">The key to use.</param>
        /// <returns>The value.</returns>
        public string? this[string key]
        {
            get => Get(key);
            set => Add(key, value);
        }




        void ReadAndDecode()
        {
            _stream.Seek(0, SeekOrigin.Begin);
            int actualLength = _stream.Read(_buffer, 0, ConfigSize);

            // at least this many bytes in the header (magic + version), or it's a new file
            if (actualLength < HeaderLength)
            {
                _isDirty = true;
            }
            else
            {
                DeobfuscateBuffer();

                if (!(_buffer[0] == 0x4d && _buffer[1] == 0x43 && _buffer[2] == 0x4f &&
                      _buffer[3] == 0x4e && _buffer[4] == 0x46))
                {
                    throw new MConfigFormatException("Invalid file format.");
                }

                //check for the only supported version and fail otherwise
                if (_buffer[5] != 0)
                {
                    throw new MConfigFormatException("Unknown version.");
                }

                for (int i = HeaderLength + 1; i < actualLength; ++i)
                {
                    //get and check key length byte
                    //this being zero indicates end of file (i.e., the zero padding)
                    byte keyLength = _buffer[i];
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
                    string key = System.Text.Encoding.UTF8.GetString(_buffer, i, keyLength);

                    _configData[key] = null;

                    //advance to value length
                    i += keyLength;
                    if (i >= actualLength) //end of container
                    {
                        break;
                    }

                    //get and check value length byte
                    //zero means null value; key present but unset
                    byte valLength = _buffer[i];
                    if (valLength != 0)
                    {
                        //check that we have that many bytes left
                        if (i + valLength >= actualLength)
                        {
                            throw new MConfigFormatException($"Value length ${valLength} truncated at {i}.");
                        }
                        //advance to start of value and copy into value buffer
                        ++i;
                        string value = System.Text.Encoding.UTF8.GetString(_buffer, i, valLength);

                        _configData[key] = value;

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
            Array.Clear(_buffer, 0, ConfigSize);

            _buffer[0] = 0x4d;
            _buffer[1] = 0x43;
            _buffer[2] = 0x4f;
            _buffer[3] = 0x4e;
            _buffer[4] = 0x46;
            _buffer[5] = CurrentVersion;

            int bufIndex = HeaderLength + 1;

            foreach (string key in _configData.Keys)
            {
                string? value = _configData[key];

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
                if (bufIndex + 1 + keyLength + 1 + valLength + 1> ConfigSize)
                {
                    throw new MConfigFormatException($"Maximum size will be exceeded at key [{key}].");
                }

                _buffer[bufIndex] = keyLength;
                ++bufIndex;
                for (int i = 0; i < keyLength; ++i)
                {
                    _buffer[bufIndex] = keyBytes[i];
                    ++bufIndex;
                }

                _buffer[bufIndex] = valLength;
                ++bufIndex;
                for (int i = 0; i < valLength; ++i)
                {
                    _buffer[bufIndex] = valBytes[i];
                    ++bufIndex;
                }
            }
            //ending byte
            _buffer[bufIndex] = 0;

            //fill rest with random
            Random rand = new Random();
            for(bufIndex += 1; bufIndex < ConfigSize; ++bufIndex)
            {
                _buffer[bufIndex] = (byte)rand.Next(0, 255);
            }

            ObfuscateBuffer();
        }

        /// <summary>
        /// Save to the backing stream.
        /// </summary>
        public void Save()
        {
            EncodeToBuffer();

            _stream.Seek(0, SeekOrigin.Begin);
            _stream.SetLength(ConfigSize);

            _stream.Write(_buffer, 0, ConfigSize);

            _isDirty = false;
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
            if (string.IsNullOrEmpty(_secret))
            {
                return;
            }
            byte[] secret = System.Text.Encoding.UTF8.GetBytes(_secret);
            int s = 0;

            for (int i = HeaderLength + 1; i < ConfigSize; ++i)
            {
                _buffer[i] = (byte)(_buffer[i] ^ secret[s]);
                ++s;
                if (s >= secret.Length)
                {
                    s = 0;
                }
            }
        }

        #region IDisposable Support
        private bool _disposedValue; // To detect redundant calls

        private void Dispose(bool disposing)
        {
            if (_disposedValue) return;
            if (disposing)
            {
                if (_isDirty)
                {
                    Save();
                }
                if (_streamIsMine)
                {
                    _stream.Close();
                }
            }

            _disposedValue = true;
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

        public bool TryGet(string key, out string? value)
        {
            if(!ContainsKey(key))
            {
                value = null;
                return false;
            }
            else
            {
                value = _configData[key];
                return true;
            }
        }

        public void Set(string key, string? value)
        {
            Add(key, value);
        }

        public IChangeToken GetReloadToken()
        {
            return null;
        }

        public void Load()
        {
            ReadAndDecode();
        }

        public IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath)
        {
            throw new NotImplementedException();
        }
    }
}