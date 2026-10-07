using Microsoft.ML.Data;

namespace EMua.Models.AI;

public class IntentPrediction
{
    [ColumnName("PredictedLabel")]
    public string Intent { get; set; } = string.Empty;

    public float[] Score { get; set; } = Array.Empty<float>();
}