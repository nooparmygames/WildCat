using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Orleans;
using Interfaces;

namespace Collections
{
    public class DeveloperInvoiceCollection : Grain<DeveloperInvoiceCollectionState>, IDeveloperInvoiceCollection
    {

#if !MASTER
        public override async Task OnActivateAsync()
        {
            State.Invoices.Add(new InvoiceData
            {
                CallCount = 300000,
                MessageCount = 3000000,
                status = InvoiceData.InvoicePaymentStatus.UnPaid,
                InvoiceId = "1",
                InvoiceDate = DateTime.Now.AddDays(-20),
                DueDate = DateTime.Now.AddDays(-8)
            });
            State.Invoices.Add(new InvoiceData
            {
                CallCount = 200000,
                MessageCount = 4000000,
                status = InvoiceData.InvoicePaymentStatus.Paid,
                InvoiceId = "2",
                InvoiceDate = DateTime.Now.AddDays(-7),
                DueDate = DateTime.Now.AddDays(-4)
            });
            await base.OnActivateAsync();
        }
#endif

        /// <summary>
        /// Generates all invoices from the beginning of the system's run if they are not in the list
        /// </summary>
        /// <returns></returns>
        public async Task<List<InvoiceData>> GenerateInvoicesIfNeeded()
        {
            //Get the developer and his/her games
            var developer = GrainFactory.GetGrain<IDeveloper>(this.GetPrimaryKey());
            var games = await developer.GetGames();

            List<InvoiceData> invoices = new List<InvoiceData>();

            //Start from March 2017 or at the month after the last invoice if we have any invoices
            DateTime startDate = new DateTime(2017, 3, 1);
            if (State.Invoices.Count > 0)
            {
                var lastGenerated = State.Invoices[0].InvoiceDate;
                startDate = lastGenerated.AddMonths(1);
            }

            //End date should be the month before the current one so we don't calculate invoices for incomplete months
            DateTime endDate = DateTime.Now.AddMonths(-1);

            //Go over all months and see if invoices are needed or not
            while (startDate <= endDate)
            {
                //Calculate the some of all game calls and messages for all titles of the developer in the month
                ulong callCount = 0;
                ulong messageCount = 0;
                foreach (var game in games)
                {
                    var gameData = await GrainFactory.GetGrain<IAccountingSystem>(game.Key, InvoiceUtility.GetInvoiceMonthKey(startDate), null).GetData();
                    callCount += gameData.CallCount;
                    messageCount += gameData.MessageCount;
                }
                //If we had any calls during the month then add it to the list
                GenerateInvoiceIfAnyCallsOrMesagesInTheMonth(invoices, startDate, callCount, messageCount);

                //Move the month one forward
                startDate.AddMonths(1);
            }
            var AddedInvoices = await AddAndSaveInvoicesIfAny(invoices);
            return AddedInvoices;
        }

        private void GenerateInvoiceIfAnyCallsOrMesagesInTheMonth(List<InvoiceData> invoices, DateTime startDate, ulong callCount, ulong messageCount)
        {
            if (callCount > 0 || messageCount > 0)
            {
                InvoiceData invoice = new InvoiceData
                {
                    CallCount = callCount,
                    MessageCount = messageCount,
                    DueDate = startDate.AddDays(2),
                    InvoiceDate = startDate,
                    InvoiceId = $"{startDate.Year}-{startDate.Month}-{this.GetPrimaryKeyString()}",
                    status = InvoiceData.InvoicePaymentStatus.UnPaid
                };
                invoices.Add(invoice);
            }
        }

        private async Task<List<InvoiceData>> AddAndSaveInvoicesIfAny(List<InvoiceData> invoices)
        {
            if (invoices.Count > 0)
            {
                for (int i = 0; i < invoices.Count; ++i)
                {
                    if (!State.Invoices.Any(x => x.InvoiceDate == invoices[i].InvoiceDate))
                    {
                        State.Invoices.Add(invoices[0]);
                    }
                }
                State.Invoices = State.Invoices.OrderByDescending(x => x.InvoiceDate).ToList();
                await WriteStateAsync();
                return invoices.OrderByDescending(x => x.InvoiceDate).ToList();
            }
            //Return an empty list if nothing is added
            return new List<InvoiceData>();
        }

        public Task ChangeInvoicePaymentStatus(string invoiceId, InvoiceData.InvoicePaymentStatus status)
        {
            var invoice = State.Invoices.FirstOrDefault(x => x.InvoiceId == invoiceId);
            if (invoice != null)
            {
                invoice.status = status;
                return WriteStateAsync();
            }
            return Task.CompletedTask;
        }

        public Task<List<InvoiceData>> GetAllInvoices()
        {
            return Task.FromResult(State.Invoices);
        }

        public Task<List<InvoiceData>> GetAllUnpaidInvoices()
        {
            var unpaidInvoices = State.Invoices.Where(x => x.status == InvoiceData.InvoicePaymentStatus.UnPaid).ToList();
            return Task.FromResult(unpaidInvoices);
        }

        public Task<string> GetAdditionalData(string invoiceId)
        {
            var invoice = State.Invoices.First(x => x.InvoiceId == invoiceId);
            return Task.FromResult(invoice.AdditionalData);
        }

        public Task SetAdditionalData(string invoiceId, string data)
        {
            var invoice = State.Invoices.First(x => x.InvoiceId == invoiceId);
            invoice.AdditionalData = data;
            return WriteStateAsync();
        }
    }
}
