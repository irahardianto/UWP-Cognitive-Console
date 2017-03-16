using Microsoft.ProjectOxford.Emotion;
using Microsoft.ProjectOxford.Emotion.Contract;
using Microsoft.ProjectOxford.Face;
using Microsoft.ProjectOxford.Face.Contract;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UWP_Cognitive_Console
{
    public class ImageAnalyzer
    {
        private IEnumerable<Face> DetectedFaces { get; set; }
        private IEnumerable<Emotion> DetectedEmotion { get; set; }

        public List<Visitor> Visitors { get; set; }
        public List<Person> CurrentPerson { get; set; }
        public List<Guid> DetectedFaceIds { get; set; }
        public List<Guid> CurrentFaceIds { get; set; }
        public int? CurrentVisitorId { get; set; }
        public int allowedPixelFrameDifference { get; set; }
        public string FaceApiKey { get; set; }
        public string EmotionApiKey { get; set; }

        public ImageAnalyzer()
        {
            FaceApiKey = ""; 
            EmotionApiKey = "";

            DetectedFaceIds = new List<Guid>();
            Visitors = new List<Visitor>();
            allowedPixelFrameDifference = 5;
        }

        public async Task DetectFaces(byte[] imgBytes = null, string path = null)
        {
            CurrentFaceIds = new List<Guid>();

            try
            {
                Stream imageFileStream = null;
                if (!string.IsNullOrEmpty(path))
                    imageFileStream = File.OpenRead(path);
                else if (imgBytes != null)
                    imageFileStream = new MemoryStream(imgBytes);

                var requiredFaceAttributes = new FaceAttributeType[] {
                        FaceAttributeType.Age,
                        FaceAttributeType.Gender,
                        FaceAttributeType.Smile,
                        FaceAttributeType.FacialHair,
                        FaceAttributeType.HeadPose,
                        FaceAttributeType.Glasses
                    };
                var faceServiceClient = new FaceServiceClient(FaceApiKey);
                var faces = await faceServiceClient.DetectAsync(
                    imageFileStream, returnFaceId: true,
                    returnFaceLandmarks: false,
                    returnFaceAttributes: requiredFaceAttributes
                    );

                var zxc = faces;

                foreach (var face in faces.ToArray())
                {
                    DetectedFaceIds.Add(face.FaceId);
                    CurrentFaceIds.Add(face.FaceId);
                }

                this.DetectedFaces = faces.ToArray();
            }
            catch (Exception ex)
            {
                this.DetectedFaces = Enumerable.Empty<Face>();
            }
        }

        public async Task DetectEmotion(byte[] imgBytes = null, string path = null)
        {
            try
            {
                Stream imageFileStream = null;
                if (!string.IsNullOrEmpty(path))
                    imageFileStream = File.OpenRead(path);
                else if (imgBytes != null)
                    imageFileStream = new MemoryStream(imgBytes);

                EmotionServiceClient emotionServiceClient = new EmotionServiceClient(EmotionApiKey);

                Emotion[] emotionResult = await emotionServiceClient.RecognizeAsync(imageFileStream);

                var asd = emotionResult;

                this.DetectedEmotion = emotionResult;
            }
            catch (Exception ex)
            {
                this.DetectedEmotion = Enumerable.Empty<Emotion>();
            }
        }

        public async Task MapFaceIdEmotionResult()
        {
            CurrentPerson = new List<Person>();

            foreach (var face in DetectedFaces)
            {
                CurrentPerson.Add(new Person
                {
                    VisitorId = Visitors.FirstOrDefault(x => x.FaceIds.Contains(face.FaceId)).Id,
                    FaceId = face.FaceId,
                    Age = (int)face.FaceAttributes.Age,
                    Gender = face.FaceAttributes.Gender,
                    Top = face.FaceRectangle.Top,
                    Left = face.FaceRectangle.Left,
                    Height = face.FaceRectangle.Height,
                    Width = face.FaceRectangle.Width,
                    HighestEmotion = SearchForMatchingEmotion(face)
                });
            }

            foreach(var vis in Visitors)
            {
                if (vis.FaceIds.Any(v => CurrentPerson.Any(x => x.FaceId == v)))
                {
                    if(!vis.DwellingTime.IsRunning) { vis.DwellingTime.Start(); }
                }
            }
        }

        public async Task FindSimilarFace()
        {
            try
            {
                var faceIdsToCompare = new List<Guid>();
                var faceServiceClient = new FaceServiceClient(FaceApiKey);

                List<Guid> identifiedPeople = new List<Guid>();

                if (Visitors.Count > 0)
                {
                    foreach (var vis in Visitors)
                    {
                        var cnt = vis.FaceIds.Count > 3 ? 3 : vis.FaceIds.Count;

                        for (int i = 0; i < cnt; i++)
                        {
                            faceIdsToCompare.Add(vis.FaceIds[i]);
                        }
                    }
                }
                else
                {
                    faceIdsToCompare = DetectedFaceIds;
                }

                foreach(var currentFaceId in CurrentFaceIds)
                {
                    var similarFace = await faceServiceClient.FindSimilarAsync(currentFaceId, faceIdsToCompare.ToArray(), 10);

                    if (similarFace.Count() > 0)
                    {
                        foreach (var visitor in Visitors)
                        {
                            if (visitor.FaceIds.AsParallel().Any(x => similarFace.AsParallel().Any(y => y.FaceId == x)))
                            {
                                if (!visitor.FaceIds.Contains(currentFaceId))
                                {
                                    visitor.FaceIds.Add(currentFaceId);
                                    visitor.LastSeen = DateTime.Now;
                                }

                                CurrentVisitorId = visitor.Id;
                            }

                            identifiedPeople.AddRange(visitor.FaceIds);
                        }

                        if (!identifiedPeople.AsParallel().Any(x => similarFace.AsParallel().Any(y => y.FaceId == x)))
                        {
                            var cnt = Visitors.Count;

                            var newVisitor = new Visitor
                            {
                                Id = cnt + 1,
                                FaceIds = new List<Guid> {
                                    currentFaceId
                                },
                                LastSeen = DateTime.Now,
                                DwellingTime = new Stopwatch()
                            };

                            CurrentVisitorId = newVisitor.Id;
                            Visitors.Add(newVisitor);
                        }
                    }
                    else
                    {
                        var cnt = Visitors.Count;

                        var newVisitor = new Visitor
                        {
                            Id = cnt + 1,
                            FaceIds = new List<Guid> {
                                currentFaceId
                            },
                            LastSeen = DateTime.Now,
                            DwellingTime = new Stopwatch()
                        };

                        CurrentVisitorId = newVisitor.Id;
                        Visitors.Add(newVisitor);
                    }
                }
            }
            catch (FaceAPIException ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.ErrorMessage);
            }
            catch (Exception ex)
            {

            }
        }

        public double GetDwellTime(int visitorId)
        {
            return Visitors.FirstOrDefault(x => x.Id == visitorId).DwellingTime.Elapsed.TotalSeconds;
        }

        public void CheckRunningDwellTime()
        {
            foreach (var vis in Visitors)
            {
                TimeSpan timeDifference = DateTime.Now - vis.LastSeen;
                TimeSpan threeSeconds = TimeSpan.FromSeconds(3);

                if (timeDifference > threeSeconds && vis.DwellingTime.IsRunning) { vis.DwellingTime.Stop(); vis.DwellingTime.Reset(); }
            }
        }

        public string SearchForMatchingEmotion(Face currentFace)
        {
            var rectangleMatch = new RectangleMatch { LikelyScore = 0, HighestDetectedEmotion = "" };

            foreach (var emo in DetectedEmotion)
            {
                var likelyMatchScore = 0;

                likelyMatchScore = isMatchBetweenTolerance(emo.FaceRectangle.Top, currentFace.FaceRectangle.Top) ? likelyMatchScore + 1 : likelyMatchScore;
                likelyMatchScore = isMatchBetweenTolerance(emo.FaceRectangle.Left, currentFace.FaceRectangle.Left) ? likelyMatchScore + 1 : likelyMatchScore;
                likelyMatchScore = isMatchBetweenTolerance(emo.FaceRectangle.Height, currentFace.FaceRectangle.Height) ? likelyMatchScore + 1 : likelyMatchScore;
                likelyMatchScore = isMatchBetweenTolerance(emo.FaceRectangle.Width, currentFace.FaceRectangle.Width) ? likelyMatchScore + 1 : likelyMatchScore;

                if (rectangleMatch.LikelyScore < likelyMatchScore)
                {
                    var expr = emo.Scores.ToRankedList();
                    var highestScore = expr.OrderByDescending(s => s.Value).First();
                    var highestEmotion = highestScore.Key;

                    rectangleMatch.LikelyScore = likelyMatchScore;
                    rectangleMatch.HighestDetectedEmotion = highestEmotion;
                }
            }

            return rectangleMatch.HighestDetectedEmotion;
        }

        public bool isMatchBetweenTolerance(int value1, int value2)
        {
            return Math.Abs(value1 - value2) <= allowedPixelFrameDifference ? true : false;
        }

        public class Visitor
        {
            public int Id { get; set; }
            public List<Guid> FaceIds { get; set; }
            public Stopwatch DwellingTime { get; set; }
            public DateTime LastSeen { get; set; }
        }

        public class Person
        {
            public int VisitorId { get; set; }
            public Guid FaceId { get; set; }
            public int Age { get; set; }
            public string Gender { get; set; }
            public string HighestEmotion { get; set; }
            public int Height { get; set; }
            public int Width { get; set; }
            public int Top { get; set; }
            public int Left { get; set; }
        }

        public class RectangleMatch
        {
            public int LikelyScore { get; set; }
            public string HighestDetectedEmotion { get; set; }
        }
    }
}
