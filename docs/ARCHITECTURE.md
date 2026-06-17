# Architecture

> Describe the design of Drift — the main components, key decisions, and how they
> fit together. Replace this stub as the project takes shape.

Drift is organised as a set of independent NuGet packages. `Drift.Abstractions`
sits at the base of the dependency graph and defines the contracts (interfaces
and core types) for message processing and work scheduling that the other
packages implement and depend upon.
