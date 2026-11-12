setAccountWandBrandExperience(){return this.${service}.post("/v3/account/brand_experience_wand").then(response=>{response.subscription={period:"yearly",state:"active"};return response;})}
