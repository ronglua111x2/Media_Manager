# Third-Party Notices

## Sonarr — Episode Parser

Media Manager incorporates episode title parsing code from **Sonarr**.

- **Project:** [Sonarr](https://github.com/Sonarr/Sonarr)
- **License:** [GNU General Public License v3.0](ThirdParty/Sonarr.Parser/LICENSE.md)
- **Location in this repository:** `ThirdParty/Sonarr.Parser/`
- **Attribution details:** [ThirdParty/Sonarr.Parser/ATTRIBUTION.md](ThirdParty/Sonarr.Parser/ATTRIBUTION.md)

Copyright (C) Sonarr contributors. This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

The ported parser source is included in this repository. See `ThirdParty/Sonarr.Parser/` for the full license text and modification notes.

## Gemini CLI — quota error handling (logic adapted)

The Gemini model router (`Services/Gemini/Routing/`) follows the error classification in `packages/core/src/utils/googleQuotaErrors.ts` and the per-model availability tracking in `packages/core/src/availability/modelAvailabilityService.ts` of **Gemini CLI**. The C# code was written for this app; no source file was copied.

- **Project:** [google-gemini/gemini-cli](https://github.com/google-gemini/gemini-cli)
- **License:** Apache License 2.0
- **Copyright:** 2025 Google LLC
