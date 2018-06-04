var artDb = db.getSiblingDB("Art");
artDb.createCollection("paintings");
artDb.createCollection("sculptures");

var paintings =
  [
    {
        "Name" : "Mona Lisa",
        "Artist" : "Leonardo da Vinci",
        "Year" : 1503,
        "Medium" : "Oil Paint"
    },
    {
        "Name" : "My first painting",
        "Artist" : "{#MyName}",
        "Year" : 2018,
        "Medium" : "Crayons"
    }    
  ];

var paintingsCollection = artDb.getCollection("paintings");
paintingsCollection.insert(paintings);