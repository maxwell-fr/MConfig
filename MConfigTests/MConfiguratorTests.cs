using MConfig;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;


namespace MConfigTests
{
    public class MConfiguratorTestData : TheoryData<IDictionary<string, string?>, string?>
    {
        public MConfiguratorTestData()
        {
            IDictionary<string, string?> dict1 = new Dictionary<string, string?>
            {
                ["key2"] = "value2",
                ["somewhat longer key again"] = "this is a value",
                ["longer key number 3"] = "longer value in slot 3",
                ["4"] = "4",
                ["empty key"] = null
            };


            IDictionary<string, string?> dict2 = new Dictionary<string, string?>
            {
                ["key1"] = "value1"
            };

            Add(dict1, null);
            Add(dict2, null);
            Add(dict1, "cryptotestkey12345678");
            Add(dict2, "shortkey");
        }
    }

    public class MConfiguratorTests
    {

        [Fact]
        public void NonexistKeyFails()
        {
            IMConfigurator mconf = new MConfigurator(new MemoryStream());

            Assert.Throws<MConfigKeyException>(() => mconf.Get("TestKey"));
        }


        [Fact]
        public void ExistingKeySucceeds()
        {
            string key = "key1";
            string value = "val1";

            IMConfigurator mconf = new MConfigurator(new MemoryStream());
            mconf.Add(key, value);
            Assert.Equal(value, mconf.Get(key));
        }

        [Theory]
        [ClassData(typeof(MConfiguratorTestData))]
        public void BracketsWork(IDictionary<string, string> dict, string secret)
        {
            MemoryStream ms = new MemoryStream();
            using (IMConfigurator mconf1 = new MConfigurator(ms, secret))
            {
                foreach (string key in dict.Keys)
                {
                    mconf1[key] = dict[key];
                }

                foreach (string key in dict.Keys)
                {
                    Assert.Equal(dict[key], mconf1[key]);
                }
            }
        }

        [Theory]
        [ClassData(typeof(MConfiguratorTestData))]
        public void ReopeningMemRetrieves(IDictionary<string, string> dict, string secret)
        {
            MemoryStream ms = new MemoryStream();
            using (IMConfigurator mconf1 = new MConfigurator(ms, secret))
            {
                foreach (string key in dict.Keys)
                {
                    mconf1.Add(key, dict[key]);
                }
            }

            using (IMConfigurator mconf2 = new MConfigurator(ms, secret))
            {
                foreach (string key in dict.Keys)
                {
                    Assert.Equal(dict[key], mconf2.Get(key));
                }
            }
        }

        [Theory]
        [ClassData(typeof(MConfiguratorTestData))]
        public void ReopeningFileRetrieves(IDictionary<string, string> dict, string secret)
        {
            string testfile = "testfile.dat";
            using (IMConfigurator mconf1 = new MConfigurator(testfile, secret))
            {
                foreach (string key in dict.Keys)
                {
                    mconf1.Add(key, dict[key]);
                }
            }

            using (IMConfigurator mconf2 = new MConfigurator(testfile, secret))
            {
                foreach (string key in dict.Keys)
                {
                    Assert.Equal(dict[key], mconf2.Get(key));
                }
            }

            File.Delete(testfile);
        }

        [Fact]
        public void MaxLengthSucceeds()
        {
            string longOne = "012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234";
            string shortOne = "test";
            Assert.Equal(Byte.MaxValue, longOne.Length);

            MemoryStream ms = new MemoryStream();
            using (IMConfigurator mconf1 = new MConfigurator(ms))
            {
                mconf1.Add(longOne, shortOne);
                mconf1.Add(shortOne, longOne);
            }

            using (IMConfigurator mconf2 = new MConfigurator(ms))
            {
                Assert.Equal(shortOne, mconf2.Get(longOne));
                Assert.Equal(longOne, mconf2.Get(shortOne));
            }
        }



        [Fact]
        public void OverLongFails()
        {
            string longOne = "0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345";
            string shortOne = "test";
            Assert.Equal(Byte.MaxValue + 1, longOne.Length);

            MemoryStream ms = new MemoryStream();
            using (IMConfigurator mconf1 = new MConfigurator(ms))
            {
                Assert.Throws<MConfigFormatException>(() => mconf1.Add(longOne, shortOne));
                Assert.Throws<MConfigFormatException>(() => mconf1.Add(shortOne, longOne));
            }

        }

        [Theory]
        [ClassData(typeof(MConfiguratorTestData))]
        public void SecretChangeWorks(IDictionary<string, string> dict, string oldsecret)
        {
            MemoryStream ms = new MemoryStream();
            string newsecret = "this is a new secret";

            using (IMConfigurator mconf1 = new MConfigurator(ms, oldsecret))
            {
                foreach (string key in dict.Keys)
                {
                    mconf1.Add(key, dict[key]);
                }
                mconf1.SetSecret(newsecret);
            }

            //old secret should fail by failing to parse the file
            //or by not finding the key if the parse does happen to succeed
            try
            {
                using (IMConfigurator mconf2 = new MConfigurator(ms, oldsecret))
                {
                    foreach (string key in dict.Keys)
                    {
                        Assert.Throws<MConfigKeyException>(() => mconf2.Get(key));
                    }
                }
            }
            catch(MConfigFormatException)
            {
                Assert.True(true);
            }

            //new secret should succeed
            using (IMConfigurator mconf3 = new MConfigurator(ms, newsecret))
            {
                foreach (string key in dict.Keys)
                {
                    Assert.Equal(dict[key], mconf3.Get(key));
                }
            }
        }
    }
}
