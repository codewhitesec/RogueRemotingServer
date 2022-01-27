using System.Runtime.Remoting.Channels;

namespace RogueRemotingServer
{
    internal class RogueServerChannelSinkProvider : IServerFormatterSinkProvider, IServerChannelSinkProvider
    {
        private readonly Protocol _protocol;
        private readonly bool _allowAnyUri;
        private readonly Format _format;
        private readonly byte[] _payload;

        public RogueServerChannelSinkProvider(Protocol protocol, bool allowAnyUri, Format format, byte[] payload)
        {
            this._protocol = protocol;
            this._allowAnyUri = allowAnyUri;
            this._format = format;
            this._payload = payload;
        }

        public IServerChannelSinkProvider Next { get; set; }

        public IServerChannelSink CreateSink(IChannelReceiver channel)
        {
            return new RogueServerChannelSink(this._protocol, this._allowAnyUri, this._format, this._payload);
        }

        public void GetChannelData(IChannelDataStore channelData) { }
    }
}