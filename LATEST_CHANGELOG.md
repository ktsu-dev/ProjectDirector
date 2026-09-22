## v1.3.0 (minor)

Changes since v1.2.0:

- refactor: filter the siblings with Where instead of an inner if [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- test: address the code-quality findings on the new tests [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- refactor: lift the comparison's rules out of the render paths [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- test: cover which comparison wins and what shows while one runs [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- fix: compare a repo against its siblings off the render thread [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- Merge main into fix/425-scan-skips-unreadable-dirs ([@Claude](https://github.com/Claude))
- fix: make the dev directory default work off Windows, and cover the load path [patch] ([@Claude](https://github.com/Claude))
- test: cover applying an owner's credentials to the shared client [patch] ([@Claude](https://github.com/Claude))
- fix: reject an unsupported saved repository at load instead of crashing [patch] ([@Claude](https://github.com/Claude))
- fix: scan each owner with its own credentials, not the previous owner's [patch] ([@Claude](https://github.com/Claude))
- fix: survive an unreadable directory while scanning the dev directory [patch] ([@Claude](https://github.com/Claude))
- refactor: pin the startup token ordering ([@Claude](https://github.com/Claude))
- refactor: pull the token rules out of the ImGui code, and test them ([@Claude](https://github.com/Claude))
- Use the collection and string assertions MSTest suggests ([@matt-edmondson](https://github.com/matt-edmondson))
- Pull the rest of the propagation rule out of the popup, and cover it ([@matt-edmondson](https://github.com/matt-edmondson))
- Move SourceLink off the version the NuGet audit fails the build on ([@matt-edmondson](https://github.com/matt-edmondson))
- Finish a file propagation the user confirmed, and say what it did ([@matt-edmondson](https://github.com/matt-edmondson))
- Gate Dependabot auto-merge on CI actually being green ([@Claude](https://github.com/Claude))
- ci: adopt the consolidated .NET workflow [patch] ([@Claude](https://github.com/Claude))

