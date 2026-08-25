// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.
document.addEventListener("DOMContentLoaded", function () {

    const cropOptions = document.querySelectorAll(".crop-option");
    const uploadSection = document.getElementById("uploadSection");
    const selectedCropMessage = document.getElementById("selectedCropMessage");
    const selectedCropName = document.getElementById("selectedCropName");

    const cropImage = document.getElementById("cropImage");
    const imagePreview = document.getElementById("imagePreview");
    const imagePreviewContainer = document.getElementById("imagePreviewContainer");

    const removeImage = document.getElementById("removeImage");
    const analyzeButton = document.getElementById("analyzeButton");


    let selectedCrop = null;


    // Crop selection
    cropOptions.forEach(function (option) {

        option.addEventListener("click", function () {

            cropOptions.forEach(function (item) {
                item.classList.remove("selected");
            });

            option.classList.add("selected");

            selectedCrop = option.dataset.crop;

            selectedCropName.textContent = selectedCrop;

            document.getElementById("selectedCrop").value = selectedCrop;

            selectedCropMessage.classList.add("visible");

            uploadSection.classList.add("visible");

        });

    });


    // Image selection
    cropImage.addEventListener("change", function () {

        const file = this.files[0];

        if (!file) {
            return;
        }

        if (!file.type.startsWith("image/")) {
            alert("Please select a valid image file.");
            cropImage.value = "";
            return;
        }

        const imageUrl = URL.createObjectURL(file);

        imagePreview.src = imageUrl;

        imagePreviewContainer.classList.add("visible");

        analyzeButton.classList.add("visible");

    });


    // Remove image
    removeImage.addEventListener("click", function () {

        cropImage.value = "";

        imagePreview.src = "#";

        imagePreviewContainer.classList.remove("visible");

        analyzeButton.classList.remove("visible");

    });

});