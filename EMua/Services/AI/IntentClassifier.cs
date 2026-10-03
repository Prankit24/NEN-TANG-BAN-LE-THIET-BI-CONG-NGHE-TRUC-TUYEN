using EMua.Models.AI;
using Microsoft.ML;

namespace EMua.Services.AI
{
    public class IntentClassifier
    {
        private readonly MLContext _mlContext;
        private readonly ITransformer _model;

        public IntentClassifier(IWebHostEnvironment environment)
        {
            _mlContext = new MLContext(seed: 1);

            var modelPath = Path.Combine(
                environment.ContentRootPath,
                "MLModels",
                "intent-model.zip");

            if (File.Exists(modelPath))
            {
                _model = _mlContext.Model.Load(modelPath, out _);
            }
        }

        public string Predict(string text)
        {
            if (_model == null) return "GeneralTechnology";

            var predictionEngine = _mlContext.Model.CreatePredictionEngine<IntentTrainingData, IntentPrediction>(_model);
            var prediction = predictionEngine.Predict(new IntentTrainingData { Text = text });

            return prediction.Intent ?? "GeneralTechnology";
        }
    }
}