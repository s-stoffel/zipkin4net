using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Owin;
using Moq;
using NSubstitute;
using NUnit.Framework;
using zipkin4net.Internal.Recorder;
using zipkin4net.Middleware.Tests.Helpers;
using zipkin4net.Tracers.Zipkin;
using zipkin4net.Transport.Http;

namespace zipkin4net.Middleware.Tests
{
    using static OwinHelper;

    public class WhenOwinMiddlewareIsActiveWithRealDispatcher
    {
        private const string SERVICE_NAME = "OwinTest";

        private Mock<IReporter> _reporter;
        private List<Span> _reportedSpans;
        private HttpMessageHandler _testHttpMessageHandler;

        [SetUp]
        public void Setup()
        {
            var logger = Substitute.For<ILogger>();

            _reporter = new Mock<IReporter>();
            _reportedSpans = new List<Span>();
            _reporter.Setup(x => x.Report(Capture.In(this._reportedSpans)));

            var tracer = new ZipkinTracer(_reporter.Object, new Statistics());

            _testHttpMessageHandler = null;

            TraceManager.SamplingRate = 1.0f;
            TraceManager.RegisterTracer(tracer);
            TraceManager.Start(logger);
        }

        [TearDown]
        public void TearDown()
        {
            TraceManager.ClearTracers();
            TraceManager.Stop();
        }

        [Test]
        public async Task Check_That_dispatcher_is_called_with_expected_records_on_GET_call()
        {
            //Arrange
            var urlToCall = new Uri("http://testserver/api/outer");

            //Act
            var responseContent = await Call(DefaultStartup(SERVICE_NAME, runHandler: RunHandler), ClientCall(urlToCall), x => _testHttpMessageHandler = x);
            Assert.IsNotEmpty(responseContent);

            Assert.AreEqual(3, _reportedSpans.Count);
            AssertSpanReported(new[] {"sr", "ss"}, "/api/outer");
            AssertSpanReported(new[] {"cs", "cr"});
            AssertSpanReported(new[] {"sr", "ss"}, "/api/inner");
        }

        private async Task RunHandler(IOwinContext context)
        {
            if (context.Request.Path.HasValue
                && context.Request.Path.Value.EndsWith("api/outer"))
            {
                // for the sake of simplicity, this is mocking that the "outer" endpoint will call some "inner" endpoint
                var uriBuilder = new UriBuilder(context.Request.Uri);
                uriBuilder.Path = uriBuilder.Path.Replace("outer", "inner");

                using (var httpClient = new HttpClient(new TracingHandler(SERVICE_NAME, _testHttpMessageHandler)))
                {
                    var response = await httpClient.GetAsync(uriBuilder.Uri);
                    var content = await response.Content.ReadAsStringAsync();

                    await context.Response.WriteAsync(content);
                }
            }

            // else: e.g. path == "api/inner"
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync(DateTime.Now.ToString());
        }

        private void AssertSpanReported(string[] annotations, string path = null)
        {
            var query = _reportedSpans.Where(span => span.Annotations != null && span.Annotations.Select(x => x.Value).SequenceEqual(annotations));
            if (path != null)
            {
                query = query.Where(span => span.BinaryAnnotations != null && span.BinaryAnnotations.Any(x => HasKeyValue(x, "http.path", path)));
            }
            var count = query.Count();
            Assert.AreEqual(1, count,
                $"Found {count} reported spans with annotations [{string.Join(",", annotations)}]{(path != null ? " and http.path '{path}'" : "")}");
        }

        private static bool HasKeyValue(BinaryAnnotation binaryAnnotation, string key, string value)
        {
            var valueAsString = Encoding.UTF8.GetString(binaryAnnotation.Value);
            return binaryAnnotation.Key == key
                   && valueAsString == value;
        }
    }
}