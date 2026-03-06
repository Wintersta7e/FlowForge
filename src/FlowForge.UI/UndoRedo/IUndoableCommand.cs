namespace FlowForge.UI.UndoRedo;

public interface IUndoableCommand
{
    string Description { get; }

    void Execute();

    void Undo();
}
