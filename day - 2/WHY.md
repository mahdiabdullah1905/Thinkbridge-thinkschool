# WHY I Made the Quote Model Rich

Before this task, the Quote class was basically just a container for Id, Author and Text. The API could create a Quote by setting its properties directly, so there was nothing inside the Quote itself stopping someone from putting invalid data into it.

Making it a rich model means the Quote now takes care of its own rules. For example, it checks that the author is between 1 and 200 characters and that the text is between 1 and 1000 characters. This means the rules are not only dependent on the API endpoint. If another part of the application creates a Quote later, it still has to follow the same rules.

I also made the quote text impossible to change after the Quote is created. This makes sense because changing the actual text would basically change what the original quote was. Instead of deleting the record from the database, the Quote can now be soft-deleted using `IsDeleted`.

One bug the old model could allow is a background job directly doing something like setting `quote.Text = ""`. If validation only existed in the API, that code could save an invalid quote. With the rich model, the Quote controls how it is created and changed, so this kind of mistake is much harder to make.
