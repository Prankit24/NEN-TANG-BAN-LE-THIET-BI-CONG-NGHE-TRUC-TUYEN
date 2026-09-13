using Microsoft.ML.Data;

namespace EMua.Models.AI
{
    public class DomainTrainingData
    {
        [LoadColumn(0)]
        public string Text { get; set; } = string.Empty;

        [LoadColumn(1)]
        public bool Label { get; set; }
    }
}