using Module.Verification.SystemFact.Observability;

namespace Module.Verification.SystemFact.Example
{
    public static class CompositionRoot
    {
        public static void Run()
        {
            UnitOfWork unitOfWork = new UnitOfWork();

            CharacterRepository repository = new CharacterRepository(unitOfWork);

            DamageLogObserver damageLogObserver = new DamageLogObserver();
            SystemFactHubBuilder factHubBuilder = new SystemFactHubBuilder();
            factHubBuilder.Register<DamageApplied>(damageLogObserver);
            SystemFactHub factHub = factHubBuilder.Build();

            ApplyDamageHandler handler = new ApplyDamageHandler(repository);

            CommandExecutor executor = new CommandExecutor(unitOfWork, factHub);

            Character character = new Character("Player001", 100);

            repository.Add(character);

            ApplyDamageCommand command = new ApplyDamageCommand("Player001", 30);

            executor.Execute(handler, command);
        }
    }
}
