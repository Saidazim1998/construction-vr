using System;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.Events;

namespace NBM500U
{
    /// <summary>
    /// Turns the select state of a Meta <see cref="PokeInteractable"/> into button events.
    /// Lives next to the PokeInteractable on the button's "_Surface" object.
    /// </summary>
    public class NBMButton : MonoBehaviour
    {
        [Tooltip("Name of the button on the machine, e.g. PS_Btn_WeldMode.")]
        [SerializeField] private string _buttonId;

        [SerializeField] private PokeInteractable _pokeInteractable;

        [SerializeField] private UnityEvent _whenPressed = new UnityEvent();
        [SerializeField] private UnityEvent _whenReleased = new UnityEvent();

        /// <summary>Raised for every button of the machine, with the button that was pressed.</summary>
        public static event Action<NBMButton> AnyPressed;

        public string ButtonId => _buttonId;
        public PokeInteractable PokeInteractable => _pokeInteractable;
        public UnityEvent WhenPressed => _whenPressed;
        public UnityEvent WhenReleased => _whenReleased;
        public bool IsPressed { get; private set; }
        public int PressCount { get; private set; }

        protected virtual void OnEnable()
        {
            if (_pokeInteractable != null)
            {
                _pokeInteractable.WhenStateChanged += HandleStateChanged;
            }
        }

        protected virtual void OnDisable()
        {
            if (_pokeInteractable != null)
            {
                _pokeInteractable.WhenStateChanged -= HandleStateChanged;
            }

            IsPressed = false;
        }

        private void HandleStateChanged(InteractableStateChangeArgs args)
        {
            if (args.NewState == InteractableState.Select)
            {
                IsPressed = true;
                PressCount++;
                _whenPressed.Invoke();
                AnyPressed?.Invoke(this);
            }
            else if (args.PreviousState == InteractableState.Select)
            {
                IsPressed = false;
                _whenReleased.Invoke();
            }
        }
    }
}
