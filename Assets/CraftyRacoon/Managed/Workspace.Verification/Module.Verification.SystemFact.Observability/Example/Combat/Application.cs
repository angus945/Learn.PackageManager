
namespace Module.Verification.SystemFact.Example
{
    public interface ICharacterRepository
    {
        Character Get(string id);
    }
    public sealed class ApplyDamageCommand
    {
        public ApplyDamageCommand(string characterId, int damage)
        {
            CharacterId = characterId;
            Damage = damage;
        }

        public string CharacterId { get; }

        public int Damage { get; }
    }
    public sealed class ApplyDamageHandler
    {
        private readonly ICharacterRepository _repository;

        public ApplyDamageHandler(ICharacterRepository repository)
        {
            _repository = repository;
        }

        public void Handle(ApplyDamageCommand command)
        {
            Character character = _repository.Get(command.CharacterId);

            character.ApplyDamage(command.Damage);
        }
    }
}