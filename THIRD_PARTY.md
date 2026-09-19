# Third-party components

## EdiabasLib

This project uses [EdiabasLib](https://github.com/uholeschak/ediabaslib) for BMW EDIABAS protocol execution and Android FTDI/K+DCAN transport.

The bootstrap scripts pin a specific EdiabasLib source revision for reproducible builds. EdiabasLib is distributed under GNU GPLv3. This repository therefore uses GPLv3 as well.

BMW ECU/SP-Daten files are not part of this repository and are not downloaded by the application. Users must provide compatible data files they are entitled to use.
