using System;

namespace OrderApi.Exceptions
{
    public class BusinessValidationException : Exception
    {
        public BusinessValidationException(string message) : base(message) { }
    }

    public class NotFoundException : Exception
    {
        public NotFoundException(string message) : base(message) { }
    }
}
