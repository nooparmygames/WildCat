namespace WildCat.Gateway
{
    /// <summary>
    /// What the dispatcher checks before it calls a handler. A refused request gets an ErrorMsg (8)
    /// with error code 1 and the request id, and never reaches the handler.
    /// </summary>
    public enum SessionRequirement
    {
        /// <summary>
        /// Nothing. A ticket in the frame header is not checked. For requests that come before a
        /// session exists (InitializeSession) or carry their ticket in the payload (the legacy
        /// sign-in messages, which check it themselves).
        /// </summary>
        None,

        /// <summary>
        /// The frame header carries the ticket of a live session (it is touched, which keeps it
        /// alive); nobody has to be logged in. <see cref="MessageContext.Session"/> is set, and
        /// <see cref="MessageContext.Player"/> too when someone is logged in on the session.
        /// </summary>
        Session,

        /// <summary>
        /// Like <see cref="Session"/>, and a player is logged in on the session.
        /// <see cref="MessageContext.Player"/> is never null. The default for game requests.
        /// </summary>
        Player,
    }
}
