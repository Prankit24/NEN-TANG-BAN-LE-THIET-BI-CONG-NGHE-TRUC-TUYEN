using EMua.Models.AI;
using Microsoft.ML;

namespace EMua.Services.AI
{
    public class DomainClassifier
    {
        private readonly MLContext _mlContext;
        private readonly ITransformer _model;

        public DomainClassifier(IWebHostEnvironment environment)
        {
            _mlContext = new MLContext();

            var modelPath = Path.Combine(
                environment.ContentRootPath,
                "MLModels",
                "domain-model.zip");

            _model = _mlContext.Model.Load(modelPath, out _);
        }

        public DomainPrediction Predict(string text)
        {
            var predictionEngine =
                _mlContext.Model.CreatePredictionEngine<
                    DomainTrainingData,
                    DomainPrediction>(_model);

            var input = new DomainTrainingData
            {
                Text = text
            };

            return predictionEngine.Predict(input);
        }
    }
}