using AuthorizeNet.Api.Contracts.V1;

namespace VirtoCommerce.AuthorizeNetPayment.Data.Extensions
{
    public static class AuthorizeApiExtensions
    {
        public static bool IsSuccessfulApiResponse(this messageTypeEnum messageType)
        {
            return messageType == messageTypeEnum.Ok;
        }
    }
}
