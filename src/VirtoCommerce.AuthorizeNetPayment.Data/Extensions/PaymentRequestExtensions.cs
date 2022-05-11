using VirtoCommerce.OrdersModule.Core.Model;
using VirtoCommerce.PaymentModule.Model.Requests;

namespace VirtoCommerce.AuthorizeNetPayment.Data.Extensions
{
    public static class PaymentRequestExtensions
    {
        public static CustomerOrder GetOrder(this PaymentRequestBase request)
        {
            return request.Order as CustomerOrder;
        }

        public static PaymentIn GetPayment(this PaymentRequestBase request)
        {
            return request.Payment as PaymentIn;
        }
    }
}
