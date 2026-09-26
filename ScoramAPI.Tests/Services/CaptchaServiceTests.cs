using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ScoramAPI.Services;
using Xunit;

namespace ScoramAPI.Tests.Services
{
    public class CaptchaServiceTests
    {
        private static CaptchaService BuildService() =>
            new(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));

        // Parses "7 + 4 = ?" back into its expected answer -- Generate() deliberately never returns
        // the answer itself (see that method's own comment on why), so this mirrors its arithmetic
        // to compute what Verify() should accept, rather than the test just trusting whatever
        // Verify() decides.
        private static int ExpectedAnswer(string question)
        {
            var match = Regex.Match(question, @"^(\d+) ([+\-×]) (\d+) = \?$");
            Assert.True(match.Success, $"Question didn't match the expected format: \"{question}\"");
            var a = int.Parse(match.Groups[1].Value);
            var op = match.Groups[2].Value;
            var b = int.Parse(match.Groups[3].Value);
            return op switch { "+" => a + b, "-" => a - b, "×" => a * b, _ => throw new InvalidOperationException() };
        }

        [Fact]
        public void Verify_SucceedsWithTheCorrectAnswer()
        {
            var service = BuildService();
            var (captchaId, question) = service.Generate();

            var result = service.Verify(captchaId, ExpectedAnswer(question));

            Assert.True(result);
        }

        [Fact]
        public void Verify_FailsWithAnIncorrectAnswer()
        {
            var service = BuildService();
            var (captchaId, question) = service.Generate();
            var wrongAnswer = ExpectedAnswer(question) + 1000; // clearly not the right answer

            var result = service.Verify(captchaId, wrongAnswer);

            Assert.False(result);
        }

        [Fact]
        public void Verify_IsOneTimeUse_EvenWithTheCorrectAnswer()
        {
            // The whole point (see the interface method's own comment): a captchaId can't be
            // replayed, whether the first attempt was right or wrong.
            var service = BuildService();
            var (captchaId, question) = service.Generate();
            var correctAnswer = ExpectedAnswer(question);

            var first = service.Verify(captchaId, correctAnswer);
            var second = service.Verify(captchaId, correctAnswer);

            Assert.True(first);
            Assert.False(second);
        }

        [Fact]
        public void Verify_ConsumesTheChallenge_EvenWhenTheFirstAttemptWasWrong()
        {
            var service = BuildService();
            var (captchaId, question) = service.Generate();
            var correctAnswer = ExpectedAnswer(question);

            var wrongAttempt = service.Verify(captchaId, correctAnswer + 1);
            var retryWithCorrectAnswer = service.Verify(captchaId, correctAnswer);

            Assert.False(wrongAttempt);
            Assert.False(retryWithCorrectAnswer); // already consumed by the wrong attempt above
        }

        [Fact]
        public void Verify_FailsForAnUnknownCaptchaId()
        {
            var service = BuildService();

            var result = service.Verify(Guid.NewGuid().ToString("N"), 42);

            Assert.False(result);
        }

        [Fact]
        public void Generate_NeverProducesANegativeSubtractionResult()
        {
            // See Generate's own comment: subtraction operands are swapped if needed so the result
            // stays non-negative -- run enough times that a '-' question is virtually certain to come
            // up at least once (roughly 1-in-3 chance per call across three operators).
            var service = BuildService();

            for (var i = 0; i < 200; i++)
            {
                var (_, question) = service.Generate();
                if (!question.Contains(" - ")) continue;

                Assert.True(ExpectedAnswer(question) >= 0, $"Subtraction question produced a negative result: \"{question}\"");
            }
        }
    }
}
