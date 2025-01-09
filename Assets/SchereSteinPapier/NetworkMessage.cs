namespace RockPaperScissors
{
    public class NetworkMessage
    {
        public string Text { get; set; }

        public string PlayerName { get; set; }

        public MessageResponseCode Code { get; set; }

        public override string ToString()
        {
            return $"[Text: '{Text}', Player: '{PlayerName}', Code: {Code.ToStringValue()}]";
        }

    }
}
