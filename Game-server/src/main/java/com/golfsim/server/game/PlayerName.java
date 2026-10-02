package com.golfsim.server.game;

import jakarta.validation.Constraint;
import jakarta.validation.ConstraintValidator;
import jakarta.validation.ConstraintValidatorContext;
import jakarta.validation.Payload;
import java.lang.annotation.ElementType;
import java.lang.annotation.Retention;
import java.lang.annotation.RetentionPolicy;
import java.lang.annotation.Target;

/** A valid player name per {@link Names}; null is invalid. */
@Target({ElementType.FIELD, ElementType.PARAMETER, ElementType.TYPE_USE, ElementType.RECORD_COMPONENT})
@Retention(RetentionPolicy.RUNTIME)
@Constraint(validatedBy = PlayerName.Validator.class)
public @interface PlayerName {

    String message() default Names.RULE;

    Class<?>[] groups() default {};

    Class<? extends Payload>[] payload() default {};

    class Validator implements ConstraintValidator<PlayerName, String> {
        @Override
        public boolean isValid(String value, ConstraintValidatorContext context) {
            return Names.problem(value).isEmpty();
        }
    }
}
