using System;
using MConfig;

namespace MConfigDemo
{
    class MConfigDemo
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Using test file: testdata.dat with secret testsecret");
            using (MConfigurator mconf = new MConfigurator("testdata.dat", "testsecret"))
            {
                Console.WriteLine("Add key1 with value val1...");
                mconf.Add("key1", "val1");
                Console.WriteLine($"Retrieve key1: {mconf.Get("key1")}");

                Console.WriteLine("Add key2 with value val2...");
                mconf.Add("key2", "val2");
                Console.WriteLine($"Retrieve key2 with brackets: {mconf["key2"]}");

                Console.WriteLine($"Key key1 is present: {mconf.ContainsKey("key1")}");

                mconf.Remove("key2");
                Console.WriteLine($"Key key2 is present after removal: {mconf.ContainsKey("key2")}");
            }
            Console.WriteLine("Closed file!");

            Console.WriteLine("Opened it again!");
            using (MConfigurator mconf = new MConfigurator("testdata.dat", "testsecret"))
            {
                Console.WriteLine($"Key key1 is present: {mconf.ContainsKey("key1")}");
                Console.WriteLine($"Retrieve key1: {mconf.Get("key1")}");
            }

            Console.WriteLine("testdata.dat is still present if you want to look at it.");
            Console.WriteLine("Press enter to exit...");
            Console.ReadKey();
        }
    }
}
