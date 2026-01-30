# Nicolay Serial SFM3x00

This is a .NET library to interact with the Nicolay flow meter USB/Serial connector meant to operate with the following Sensirion flow meters:

- SFM3200-AW
- SFM3300-AW
- SFM3300-D
- SFM3400-AW
- SFM3400-D

Early testing using the event-based serial primitives at the default 115200 baud rate shows an effective capture rate of about 60Hz when requesting each measurement individually.
