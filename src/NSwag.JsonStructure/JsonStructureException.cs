//-----------------------------------------------------------------------
// <copyright file="JsonStructureException.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/RicoSuter/NSwag/blob/master/LICENSE.md</license>
//-----------------------------------------------------------------------

namespace NSwag.JsonStructure
{
    /// <summary>An error raised while reading, resolving or generating from a JSON Structure schema.</summary>
    public class JsonStructureException : Exception
    {
        /// <summary>Initializes a new instance of the <see cref="JsonStructureException"/> class.</summary>
        public JsonStructureException()
        {
        }

        /// <summary>Initializes a new instance of the <see cref="JsonStructureException"/> class.</summary>
        /// <param name="message">The message.</param>
        public JsonStructureException(string message)
            : base(message)
        {
        }

        /// <summary>Initializes a new instance of the <see cref="JsonStructureException"/> class.</summary>
        /// <param name="message">The message.</param>
        /// <param name="innerException">The inner exception.</param>
        public JsonStructureException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>Initializes a new instance of the <see cref="JsonStructureException"/> class.</summary>
        /// <param name="message">The message.</param>
        /// <param name="pointer">The JSON Pointer identifying where the error occurred.</param>
        public JsonStructureException(string message, string pointer)
            : base(pointer != null ? message + " (at " + pointer + ")" : message)
        {
            Pointer = pointer;
        }

        /// <summary>Initializes a new instance of the <see cref="JsonStructureException"/> class.</summary>
        /// <param name="message">The message.</param>
        /// <param name="pointer">The JSON Pointer identifying where the error occurred.</param>
        /// <param name="innerException">The inner exception.</param>
        public JsonStructureException(string message, string pointer, Exception innerException)
            : base(pointer != null ? message + " (at " + pointer + ")" : message, innerException)
        {
            Pointer = pointer;
        }

        /// <summary>Gets the JSON Pointer identifying where the error occurred.</summary>
        public string Pointer { get; }    }
}
