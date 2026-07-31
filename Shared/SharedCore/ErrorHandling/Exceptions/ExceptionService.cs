namespace Shared.Core.ErrorHandling.Exceptions
{
    public interface IExceptionService
    {
        ExceptionInformation Create(Exception e);
        ExceptionInformation Create(Exception e, string userMessage);
    }

    class ExceptionService : IExceptionService
    {
        private readonly IEnumerable<IExceptionInformationProvider> _informationProviders;


        public ExceptionService(IEnumerable<IExceptionInformationProvider> informationProviders)
        {
            _informationProviders = informationProviders;

        }

        public ExceptionInformation Create(Exception e) =>
            Create(e, string.Empty);

        public ExceptionInformation Create(Exception e, string userMessage)
        {
            var exceptionList = UnrollException(e);
            var extendedException = new ExceptionInformation
            {
                ExceptionInfo = exceptionList.ToArray(),
                UserMessage = userMessage,
            };

            foreach (var provider in _informationProviders)
                provider.HydrateExcetion(extendedException);

            return extendedException;
        }


        static List<ExceptionInstance> UnrollException(Exception e)
        {
            var output = new List<ExceptionInstance>();

            var innerE = e;
            while (innerE != null)
            {
                var splitTrace = new string[0];
                if (innerE.StackTrace != null)
                    splitTrace = innerE.StackTrace.Split("\n");

                output.Add(new ExceptionInstance(innerE.Message, splitTrace));
                innerE = innerE.InnerException;
            }

            return output;
        }
    }
}
