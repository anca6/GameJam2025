public interface IEventPuzzle
{
    // Called right after instantiate so the puzzle can randomize its state.
    void InitPuzzle();
    // EventInstance subscribes to this to know when the puzzle is solved.
    event System.Action OnSolved;
}
