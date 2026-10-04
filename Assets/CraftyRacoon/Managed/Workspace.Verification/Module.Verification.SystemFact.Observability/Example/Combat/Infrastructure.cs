using System.Collections.Generic;

namespace Module.Verification.SystemFact.Example
{
    public sealed class UnitOfWork
    {
        private readonly List<ISystemFactSource> _trackedSources = new List<ISystemFactSource>();

        public void Track(ISystemFactSource source)
        {
            if (_trackedSources.Contains(source))
            {
                return;
            }

            _trackedSources.Add(source);
        }

        public void SaveChanges()
        {
            // DB / persistence commit
        }

        public IReadOnlyCollection<ISystemFact> ReleaseFacts()
        {
            List<ISystemFact> facts = new List<ISystemFact>();

            foreach (ISystemFactSource source in _trackedSources)
            {
                IReadOnlyCollection<ISystemFact> sourceFacts = source.ReleaseFacts();

                foreach (ISystemFact fact in sourceFacts)
                {
                    facts.Add(fact);
                }
            }

            _trackedSources.Clear();

            return facts;
        }
    }
    public sealed class CharacterRepository : ICharacterRepository
    {
        private readonly Dictionary<string, Character> _characters;
        private readonly UnitOfWork _unitOfWork;

        public CharacterRepository(UnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
            _characters = new Dictionary<string, Character>();
        }

        public void Add(Character character)
        {
            _characters.Add(character.Id, character);
        }

        public Character Get(string id)
        {
            Character character = _characters[id];

            _unitOfWork.Track(character);

            return character;
        }
    }
}