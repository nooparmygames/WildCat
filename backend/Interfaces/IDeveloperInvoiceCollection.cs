using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;

namespace Interfaces
{
    public interface IDeveloperInvoiceCollection : IGrainWithGuidKey
    {
        Task<List<InvoiceData>> GetAllInvoices();
        Task<List<InvoiceData>> GetAllUnpaidInvoices();
        Task<List<InvoiceData>> GenerateInvoicesIfNeeded();
        Task ChangeInvoicePaymentStatus(string invoiceId, InvoiceData.InvoicePaymentStatus status);


        Task<string> GetAdditionalData(string invoiceId);
        Task SetAdditionalData(string invoiceId, string data);
    }

    [Serializable]
    public class InvoiceData
    {
        public const ulong CALL_BATCH_PRICE = 35;
        public const ulong MESSAGE_BATCH_PRICE = 35;
        public const ulong CALL_BATCH_SIZE = 1000;
        public const ulong MESSAGE_BATCH_SIZE = 10000;

        public enum InvoicePaymentStatus { Paid, UnPaid, ProcessingPayment, Cancelled }

        public InvoicePaymentStatus status;
        public string InvoiceId { get; set; }
        public DateTime InvoiceDate { get; set; }
        public DateTime DueDate { get; set; }
        public ulong CallCount { get; set; }
        public ulong MessageCount { get; set; }

        /// <summary>
        /// Used by the payment system to store arbitrary data
        /// </summary>
        public string AdditionalData { get; set; }
        /// <summary>
        /// Cacluates the price of the invoice
        /// </summary>
        public ulong Price
        {
            get
            {
                return (MessageCount / MESSAGE_BATCH_SIZE * MESSAGE_BATCH_PRICE) + (CallCount / CALL_BATCH_SIZE * CALL_BATCH_PRICE);
            }
        }
    }

    [Serializable]
    public class DeveloperInvoiceCollectionState
    {
        public List<InvoiceData> Invoices { get; set; }
    }
}
