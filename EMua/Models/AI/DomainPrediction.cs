using Microsoft.ML.Data;

namespace EMua.Models.AI
{
    public class DomainPrediction
    {
        [ColumnName("PredictedLabel")]
        public bool IsTechnology { get; set; }

        public float Probability { get; set; }

        public float Score { get; set; }
    }
}