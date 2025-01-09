using System;

namespace RockPaperScissors
{
    public static class MessageResponseCodeExtensions
    {
        public static string ToStringValue(this MessageResponseCode code)
        {
            return code switch
            {
                MessageResponseCode.ACK => "Acknowledgement",
                MessageResponseCode.END => "End",
                MessageResponseCode.REF => "Reference",
                MessageResponseCode.SOL => "Solution",
                MessageResponseCode.MES => "Message",
                MessageResponseCode.CON => "Connection",
                _ => throw new ArgumentOutOfRangeException(nameof(code), code, null)
            };
        }
    }
}