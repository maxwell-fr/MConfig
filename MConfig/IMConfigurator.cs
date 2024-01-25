using System;
using Microsoft.Extensions.Configuration;

namespace MConfig
{
    public interface IMConfigurator : IDisposable, IConfigurationProvider
    {
        int Count { get; }

        string? this[string key] { get; set; }

        string? Get(string key);

        void Add(string key, string? value);

        void Remove(string key);

        bool ContainsKey(string key);
        
        void Save();

        void SetSecret(string secret);

    }
}