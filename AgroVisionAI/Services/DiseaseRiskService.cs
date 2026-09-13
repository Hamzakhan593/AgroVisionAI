using AgroVisionAI.Models.WeatherRisk;

namespace AgroVisionAI.Services
{
    public sealed class DiseaseRiskService : IDiseaseRiskService
    {
        public IReadOnlyList<DiseaseRiskAssessment> Assess(
            string crop,
            WeatherSnapshot weather)
        {
            return crop.Trim().ToLowerInvariant() switch
            {
                "cotton" => AssessCotton(weather),
                "wheat" => AssessWheat(weather),
                "rice" => AssessRice(weather),
                _ => throw new ArgumentException("Supported crops are Cotton, Wheat and Rice.", nameof(crop))
            };
        }

        private static IReadOnlyList<DiseaseRiskAssessment> AssessCotton(WeatherSnapshot weather)
        {
            return new[]
            {
                BuildFungalOrBacterialRisk(
                    disease: "Cotton Bacterial Blight",
                    weather,
                    idealTempMin: 25,
                    idealTempMax: 32,
                    acceptableTempMin: 20,
                    acceptableTempMax: 35,
                    highHumidity: 80,
                    moderateHumidity: 65,
                    highRainMm: 8,
                    moderateRainMm: 2,
                    scoutingAdvice: "Check leaves for angular, water-soaked to dark lesions and inspect new growth after wet weather."),

                BuildVectorProxyRisk(
                    disease: "Cotton Leaf Curl Virus",
                    weather,
                    warmMin: 24,
                    warmMax: 36,
                    vectorName: "whitefly",
                    scoutingAdvice: "Inspect young leaves for curling, vein thickening and whiteflies, especially on field edges.")
            };
        }

        private static IReadOnlyList<DiseaseRiskAssessment> AssessWheat(WeatherSnapshot weather)
        {
            return new[]
            {
                BuildFungalOrBacterialRisk(
                    disease: "Wheat Yellow Rust",
                    weather,
                    idealTempMin: 7,
                    idealTempMax: 18,
                    acceptableTempMin: 5,
                    acceptableTempMax: 22,
                    highHumidity: 80,
                    moderateHumidity: 70,
                    highRainMm: 5,
                    moderateRainMm: 1,
                    scoutingAdvice: "Scout for yellow-orange pustules arranged in narrow stripes, especially on younger leaves."),

                BuildFungalOrBacterialRisk(
                    disease: "Wheat Brown Rust",
                    weather,
                    idealTempMin: 15,
                    idealTempMax: 25,
                    acceptableTempMin: 10,
                    acceptableTempMax: 30,
                    highHumidity: 75,
                    moderateHumidity: 65,
                    highRainMm: 5,
                    moderateRainMm: 1,
                    scoutingAdvice: "Inspect leaves for scattered orange-brown pustules and compare several plants across the field.")
            };
        }

        private static IReadOnlyList<DiseaseRiskAssessment> AssessRice(WeatherSnapshot weather)
        {
            return new[]
            {
                BuildFungalOrBacterialRisk(
                    disease: "Rice Bacterial Leaf Blight",
                    weather,
                    idealTempMin: 25,
                    idealTempMax: 34,
                    acceptableTempMin: 20,
                    acceptableTempMax: 36,
                    highHumidity: 82,
                    moderateHumidity: 70,
                    highRainMm: 10,
                    moderateRainMm: 3,
                    scoutingAdvice: "Check leaf tips and margins for water-soaked streaks that become yellow to straw coloured."),

                BuildFungalOrBacterialRisk(
                    disease: "Rice Brown Spot",
                    weather,
                    idealTempMin: 20,
                    idealTempMax: 30,
                    acceptableTempMin: 17,
                    acceptableTempMax: 34,
                    highHumidity: 80,
                    moderateHumidity: 68,
                    highRainMm: 7,
                    moderateRainMm: 2,
                    scoutingAdvice: "Look for oval brown lesions with grey or lighter centres on several leaves."),

                BuildFungalOrBacterialRisk(
                    disease: "Rice Leaf Blast",
                    weather,
                    idealTempMin: 20,
                    idealTempMax: 28,
                    acceptableTempMin: 16,
                    acceptableTempMax: 32,
                    highHumidity: 85,
                    moderateHumidity: 75,
                    highRainMm: 7,
                    moderateRainMm: 2,
                    scoutingAdvice: "Scout for spindle-shaped lesions with grey centres, especially where leaves remain wet or humid."),

                BuildVectorProxyRisk(
                    disease: "Rice Tungro",
                    weather,
                    warmMin: 24,
                    warmMax: 34,
                    vectorName: "green leafhopper",
                    scoutingAdvice: "Check for yellow-orange discoloration, stunting and green leafhopper activity in and around the field.")
            };
        }

        private static DiseaseRiskAssessment BuildFungalOrBacterialRisk(
            string disease,
            WeatherSnapshot weather,
            double idealTempMin,
            double idealTempMax,
            double acceptableTempMin,
            double acceptableTempMax,
            double highHumidity,
            double moderateHumidity,
            double highRainMm,
            double moderateRainMm,
            string scoutingAdvice)
        {
            var temperature = weather.CurrentTemperatureC;
            var humidity = weather.Next24HoursAverageHumidityPercent;
            var rain = weather.Next72HoursPrecipitationMm;

            var score = 0;
            var reasons = new List<string>();

            if (temperature >= idealTempMin && temperature <= idealTempMax)
            {
                score += 40;
                reasons.Add($"Temperature around {temperature:F1}°C is within a favorable range for this disease pattern.");
            }
            else if (temperature >= acceptableTempMin && temperature <= acceptableTempMax)
            {
                score += 24;
                reasons.Add($"Temperature around {temperature:F1}°C is partly favorable.");
            }
            else
            {
                reasons.Add($"Temperature around {temperature:F1}°C is less favorable for this disease pattern.");
            }

            if (humidity >= highHumidity)
            {
                score += 35;
                reasons.Add($"Average 24-hour humidity is high ({humidity:F0}%).");
            }
            else if (humidity >= moderateHumidity)
            {
                score += 22;
                reasons.Add($"Average 24-hour humidity is moderately favorable ({humidity:F0}%).");
            }
            else
            {
                reasons.Add($"Average 24-hour humidity is relatively low ({humidity:F0}%).");
            }

            if (rain >= highRainMm)
            {
                score += 25;
                reasons.Add($"About {rain:F1} mm precipitation is forecast over the next 72 hours, increasing leaf wetness risk.");
            }
            else if (rain >= moderateRainMm || weather.Next72HoursMaximumPrecipitationProbabilityPercent >= 55)
            {
                score += 15;
                reasons.Add("Some rainfall or a meaningful chance of precipitation may support leaf wetness.");
            }
            else
            {
                reasons.Add("Little rainfall is forecast, so prolonged leaf wetness is less likely from rain alone.");
            }

            score = Math.Clamp(score, 0, 100);
            var level = ToRiskLevel(score);

            return new DiseaseRiskAssessment
            {
                Disease = disease,
                Score = score,
                Level = level,
                Summary = BuildSummary(level),
                Reasons = reasons,
                Advice = BuildAdvice(level, scoutingAdvice),
                IsWeatherProxyOnly = false
            };
        }

        private static DiseaseRiskAssessment BuildVectorProxyRisk(
            string disease,
            WeatherSnapshot weather,
            double warmMin,
            double warmMax,
            string vectorName,
            string scoutingAdvice)
        {
            var score = 15;
            var reasons = new List<string>
            {
                $"{disease} is mainly spread by an insect vector ({vectorName}); weather alone cannot estimate infection probability."
            };

            if (weather.CurrentTemperatureC >= warmMin && weather.CurrentTemperatureC <= warmMax)
            {
                score += 25;
                reasons.Add($"Warm conditions ({weather.CurrentTemperatureC:F1}°C) may support vector activity.");
            }
            else
            {
                reasons.Add($"Current temperature ({weather.CurrentTemperatureC:F1}°C) is outside the simple warm-weather proxy used here.");
            }

            if (weather.Next24HoursAverageHumidityPercent >= 65)
            {
                score += 10;
                reasons.Add("Moderate-to-high humidity may support crop and insect activity, but this is only an indirect signal.");
            }

            if (weather.Next72HoursPrecipitationMm < 20)
            {
                score += 8;
            }

            // Never label a vector-borne disease as High using weather alone.
            score = Math.Clamp(score, 0, 59);
            var level = ToRiskLevel(score);

            return new DiseaseRiskAssessment
            {
                Disease = disease,
                Score = score,
                Level = level,
                Summary = "Weather-only proxy. Confirm vector presence and field symptoms before making any disease decision.",
                Reasons = reasons,
                Advice = BuildAdvice(level, scoutingAdvice),
                IsWeatherProxyOnly = true
            };
        }

        private static DiseaseRiskLevel ToRiskLevel(int score)
        {
            if (score >= 70) return DiseaseRiskLevel.High;
            if (score >= 40) return DiseaseRiskLevel.Moderate;
            return DiseaseRiskLevel.Low;
        }

        private static string BuildSummary(DiseaseRiskLevel level) => level switch
        {
            DiseaseRiskLevel.High => "Weather conditions are strongly favorable for this disease pattern. Increase field scouting.",
            DiseaseRiskLevel.Moderate => "Some weather conditions are favorable. Monitor the crop closely.",
            _ => "Current forecast is less favorable for this disease pattern, but field symptoms should still be checked."
        };

        private static IReadOnlyList<string> BuildAdvice(
            DiseaseRiskLevel level,
            string scoutingAdvice)
        {
            var advice = new List<string> { scoutingAdvice };

            if (level == DiseaseRiskLevel.High)
            {
                advice.Add("Inspect multiple areas of the field today and again after rain or prolonged humidity.");
            }
            else if (level == DiseaseRiskLevel.Moderate)
            {
                advice.Add("Repeat scouting within 24–48 hours if humid or wet conditions continue.");
            }
            else
            {
                advice.Add("Continue routine scouting; low weather favorability does not rule out existing disease.");
            }

            advice.Add("Use crop symptoms or an AgroVision image analysis for diagnosis; do not treat based on weather risk alone.");
            return advice;
        }
    }
}
