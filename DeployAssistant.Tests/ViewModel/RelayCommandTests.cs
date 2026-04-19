using DeployAssistant.ViewModel.Utils;
using System.Windows.Input;
using Xunit;

namespace DeployAssistant.Tests.ViewModel
{
    public class RelayCommandTests
    {
        [Fact]
        public void Constructor_NullExecute_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new RelayCommand(null!));
        }

        [Fact]
        public void CanExecute_WithNullPredicate_AlwaysReturnsTrue()
        {
            var cmd = new RelayCommand(_ => { });

            Assert.True(cmd.CanExecute(null));
            Assert.True(cmd.CanExecute("anything"));
        }

        [Fact]
        public void CanExecute_WithPredicate_ReturnsFalse_WhenPredicateFalse()
        {
            var cmd = new RelayCommand(_ => { }, _ => false);

            Assert.False(cmd.CanExecute(null));
        }

        [Fact]
        public void CanExecute_WithPredicate_ReturnsTrue_WhenPredicateTrue()
        {
            var cmd = new RelayCommand(_ => { }, _ => true);

            Assert.True(cmd.CanExecute(null));
        }

        [Fact]
        public void Execute_InvokesAction_WithParameter()
        {
            object? received = null;
            var cmd = new RelayCommand(p => received = p);

            cmd.Execute("hello");

            Assert.Equal("hello", received);
        }

        [Fact]
        public void Execute_InvokesAction_WithNullParameter()
        {
            object? received = new object(); // non-null sentinel
            var cmd = new RelayCommand(p => received = p);

            cmd.Execute(null);

            Assert.Null(received);
        }

        [Fact]
        public void Execute_NotCalledWhen_CanExecuteIsFalse_VerifiedManually()
        {
            // RelayCommand.Execute does NOT internally gate on CanExecute (WPF binding does).
            // Verify this behavior: Execute still runs even when predicate returns false.
            bool executed = false;
            var cmd = new RelayCommand(_ => executed = true, _ => false);

            cmd.Execute(null);

            Assert.True(executed);
        }

        [Fact]
        public void CanExecuteChanged_SubscribeAndUnsubscribe_DoesNotThrow()
        {
            var cmd = new RelayCommand(_ => { });
            EventHandler handler = (_, _) => { };

            var ex = Record.Exception(() =>
            {
                cmd.CanExecuteChanged += handler;
                cmd.CanExecuteChanged -= handler;
            });

            Assert.Null(ex);
        }
    }
}
