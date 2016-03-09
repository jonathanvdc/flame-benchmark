using System;

namespace Flame.Benchmark
{
    /// <summary>
    /// A data structure that contains method references pertaining to the 
    /// test harness.
    /// </summary>
    public class Harness
    {
        public Harness(
            IMethod StartBenchmarkMethod, IMethod StartIterationMethod, 
            IMethod EndBenchmarkMethod, IMethod EndIterationMethod)
        {
            this.StartBenchmarkMethod = StartBenchmarkMethod;
            this.StartIterationMethod = StartIterationMethod;
            this.EndBenchmarkMethod = EndBenchmarkMethod;
            this.EndIterationMethod = EndIterationMethod;
        }

        /// <summary>
        /// Gets the start-benchmark method.
        /// </summary>
        /// <value>The start-benchmark method.</value>
        public IMethod StartBenchmarkMethod { get; private set; }

        /// <summary>
        /// Gets the start-iteration method. This method may or may not be present.
        /// </summary>
        /// <value>The start-iteration method.</value>
        public IMethod StartIterationMethod { get; private set; }

        /// <summary>
        /// Gets the end-benchmark method.
        /// </summary>
        /// <value>The end-benchmark method.</value>
        public IMethod EndBenchmarkMethod { get; private set; }

        /// <summary>
        /// Gets the end-iteration method. This method may or may not be present.
        /// </summary>
        /// <value>The end-iteration method.</value>
        public IMethod EndIterationMethod { get; private set; }
    }
}

