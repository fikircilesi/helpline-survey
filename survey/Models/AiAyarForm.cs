using System;

namespace survey.Models
{
    public class AiAyarForm
    {
        public int AiAyarId { get; set; }
        public string Provider { get; set; } = "OpenAI";
        public string Endpoint { get; set; } = "https://api.openai.com/v1";
        public string ChatModel { get; set; } = "gpt-4o-mini";
        public string EmbeddingModel { get; set; } = "text-embedding-3-small";
        public string ApiKey { get; set; }
        public string ApiKeyMasked { get; set; }
        public bool Aktif { get; set; } = true;
        public DateTime? GuncellemeTarihi { get; set; }
        public bool TableReady { get; set; }
    }
}
