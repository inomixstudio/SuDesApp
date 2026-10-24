# Letter input architecture

`BaseSuratInputViewModel` owns the observable state shared by letter forms and
coordinates loading and saving through the existing repositories. The focused
`WargaFormMapper` owns normalization of nullable resident fields into values
ready for form binding. The ViewModel remains responsible for UI date
presentation and for converting form state to the persistence model, since
those decisions depend on the form's save policy.

Specific letter ViewModels extend the shared form state with document-specific
fields and data collection. Views bind to those ViewModels; repository access
flows from the ViewModel through `IUnitOfWork`.

