# Professional Setup Instructions


- [Features](#features)
- [Commands](#commands)
- [Demos](#demos)
- [Setup Instructions](#installation-process)
- [Command Reference](#command-reference)
- [Licence](#licence)

# Features

- High-Speed Interface
- Efficient search (without latency) and quick access to Notion items (pages, database and relations)
- Support for databases, multi selections, relations and date properties
- Quick create database items with custom supported properties
- Optional icons for Notion items, with the flexibility to disable them
- Support for custom payload to search or Edit
- Open-source for transparency
- All data is cached and stored locally for optimal performance
- Prioritizing user privacy and security

# Commands

- `@` To select database
- `!` To select relation
- `#` To select Tag (multi select only) support multiple tags
- `[` To add a link
- `*` or `^` To insert a block
- `$` Used by auto complete `Tab Key` to change mode to search filter when auto complete a database or relation
- `$` Used by auto complete `Tab Key` to change mode to Edit when auto complete a database item
- Date is automatically selected once it is recognized.

## Installation Process
1. **Plugin Installation:**
   - Start by installing the plugin.

2. **Plugin Activation:**
   - Trigger the plugin using the action keyword `c`.
   - Click to open the settings panel.

     ![No API Image](assets/screenshots/ErrorIIT.png)

### Configuration Steps

   ![Configuration Steps](assets/gif/ConfigurationSteps.gif)

3. **Navigate to Settings:**
   - Within the settings, navigate to `Plugins > Notion`.

4. **Integration Token Setup:**
   - Paste your Internal Integration Token.
   - [Create a new token](https://www.notion.so/my-integrations) if necessary.

   
      > **Notes:**
      > - Ensure that the Internal Integration Token Content Capabilities include Read, Update, and Insert content.
      >   
      >     ![Token Capabilities Image](assets/screenshots/TokenCapabilities.png)
      >
      > - Share at least one database with the token.
      >    - To share a database, go to the Database page and select your integration name.
      >      
      >     ![Full Database Sharing Image](assets/screenshots/FullDB.png)

5. **Database Query:**
   - Trigger the plugin again after setting the Internal Integration Token.
   - Wait while the plugin queries the databases.

6. **Testing Databases:**
   - Test the databases using the command `c @`.
    
     ![Choose Relation Database Image](assets/gif/DatabaseSelection.gif)

   - Confirm that your databases shared with the token are visible.

8. **Select Relation Database:**
   - Navigate to `Settings > Plugins > Notion`.
   - Choose your relational database and await the success message.

   > ![Choose Relation Database Image](assets/screenshots/RelationSelection.png)

9. Finally restart Flow Launcher.


