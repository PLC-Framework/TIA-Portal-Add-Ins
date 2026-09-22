namespace Core.Repo
{
    /// <summary>
    /// What the comparison has to say about one object. **A list, not a verdict**: a block can
    /// be outdated *and* in the wrong folder at once, and collapsing that into one word would
    /// lose half of what has to be fixed.
    /// </summary>
    public enum Finding
    {
        /// <summary>
        /// The core marks this version <c>deprecated</c>. <see cref="ComparedObject.Replacement"/>
        /// is what its <c>deprecatedBy</c> names.
        ///
        /// **Nothing compares one version as greater than another.** The core says what it still
        /// stands behind, and a base with two live majors - which the real core had until it was
        /// tidied - is then two right answers rather than one outdated block.
        /// </summary>
        Outdated,

        /// <summary>
        /// The core does not define this version. <see cref="ComparedObject.Versions"/> holds the
        /// versions it does define, and is **empty when the core has never heard of the name at
        /// all** - which is what a block claiming a `core/` family it is not entitled to looks
        /// like.
        /// </summary>
        UnknownVersion,

        /// <summary>
        /// It is not in the folder its family names. <see cref="ComparedObject.Expected"/> says
        /// where it should be — <c>core/adt/queue</c>, under whichever TIA tree it belongs to.
        ///
        /// **This became answerable when the destination of a download was settled.** Until
        /// then the core's families and the project's folders had no correspondence, and the
        /// most that could be said was that a family sat in more than one place.
        /// </summary>
        Misplaced,

        /// <summary>
        /// The <c>TITLE</c> metadata and TIA's own <c>VERSION</c> header do not say the same
        /// thing. Neither is believed over the other here: the block says two things and what it
        /// needs is an edit.
        /// </summary>
        Disagrees,

        /// <summary>
        /// No metadata and no <c>core/</c> family - the plant's own block, which in a real
        /// project is most of them. It is not a problem; it is why everything is mapped.
        /// </summary>
        NotFromCore
    }
}
