namespace LOLProximityVC
{
    public static class Packets
    {
        public const byte CONNECT = 0x01;
        public const byte AUDIO = 0x02;
        public const byte KEEPALIVE = 0x03;
        public const byte DISCONNECT = 0x04;
        public const byte ACK = 0xA1;
        public const byte REJECT = 0xA2;
        public const byte CLIENTS = 0xA3;
        public const byte LEVELS = 0xA4;
        public const byte MODE = 0xA5;          // Server -> Client: "discord" or "voip"
        public const byte DISCORD_ID = 0xA6;    // Client -> Server: discord user id
        public const byte VOLUMES = 0xA7;       // Server -> Client: discordId:volume pairs
    }
}