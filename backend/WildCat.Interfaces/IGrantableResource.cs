using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WildCat.Interfaces
{
    public interface IGrantableResource
    {
        Task<List<PlayerInventoryState.InInventoryItemState>> Grant();

    }
}
