using System;
using Module.Verification.SystemFact.Observability;

namespace Module.Verification.SystemFact.Example
{
    public sealed class DamageLogObserver : ISystemFactObserver<DamageApplied>
    {
        public void Observe(DamageApplied fact, FactObservationContext context)
        {
            Console.WriteLine($"[{context.Sequence}] DamageApplied Character={fact.CharacterId} HP={fact.PreviousHp}->{fact.CurrentHp} Damage={fact.Damage}");
        }
    }
}
