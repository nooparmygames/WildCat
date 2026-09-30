using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Interfaces
{
    public class InvoiceUtility
    {
        public static string GetInvoiceMonthKey(DateTime dt)
        {
            return $"{dt.Month}-{dt.Year}";
        }
    }
}
