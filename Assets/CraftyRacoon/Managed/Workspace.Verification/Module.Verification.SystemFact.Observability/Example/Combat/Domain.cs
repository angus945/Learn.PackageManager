using System.Collections.Generic;

namespace Module.Verification.SystemFact.Example
{
    public abstract class AggregateRoot : ISystemFactSource
    {
        private readonly List<ISystemFact> _facts = new List<ISystemFact>();

        protected void RaiseFact(ISystemFact fact)
        {
            _facts.Add(fact);
        }

        public IReadOnlyCollection<ISystemFact> ReleaseFacts()
        {
            ISystemFact[] facts = _facts.ToArray();
            _facts.Clear();

            return facts;
        }
    }
    public sealed class Character : AggregateRoot
    {
        public Character(string id, int hp)
        {
            Id = id;
            Hp = hp;
        }

        public string Id { get; }

        public int Hp { get; private set; }

        public void ApplyDamage(int damage)
        {
            int previousHp = Hp;

            Hp -= damage;

            if (Hp < 0)
            {
                Hp = 0;
            }

            RaiseFact(new DamageApplied(Id, previousHp, Hp, damage));
        }
    }
    public sealed class DamageApplied : IDomainFact
    {
        public DamageApplied(string characterId, int previousHp, int currentHp, int damage)
        {
            CharacterId = characterId;
            PreviousHp = previousHp;
            CurrentHp = currentHp;
            Damage = damage;
        }

        public string CharacterId { get; }

        public int PreviousHp { get; }

        public int CurrentHp { get; }

        public int Damage { get; }
    }
}